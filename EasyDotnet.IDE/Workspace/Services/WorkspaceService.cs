using System.CommandLine.Parsing;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Workspace.Controllers;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceService(
    WorkspaceProjectResolver resolver,
    WorkspaceSessionManager sessionManager,
    WorkspacePreBuildService preBuildService,
    IEditorService editorService,
    WorkspaceBuildHostManager buildHostManager,
    IDebugOrchestrator debugOrchestrator,
    IDebugStrategyFactory debugStrategyFactory,
    ILogger<WorkspaceService> logger)
{
  public async Task RunAsync(WorkspaceRunRequest request, CancellationToken ct)
  {
    if (!ValidateFilePath(request.FilePath))
    {
      return;
    }

    var target = await resolver.ResolveAsync(
        request.FilePath, request.UseDefault, request.UseLaunchProfile, "run", ct);
    if (target is null)
    {
      return;
    }

    if (target.Kind == ExecutionTargetKind.SingleFile)
    {
      await RunSingleFileAsync(target.SingleFilePath!, request.CliArgs, ct);
      return;
    }

    var project = target.Project!;
    await DispatchRunAsync(project, target.LaunchProfile, request.CliArgs, ct);
  }

  public async Task DebugAsync(WorkspaceDebugRequest request, CancellationToken ct)
  {
    try
    {
      if (!ValidateFilePath(request.FilePath))
      {
        return;
      }

      var target = await resolver.ResolveAsync(
          request.FilePath, request.UseDefault, request.UseLaunchProfile, "debug", ct);
      if (target is null)
      {
        return;
      }

      if (target.Kind == ExecutionTargetKind.SingleFile)
      {
        await DebugSingleFileAsync(target.SingleFilePath!, request.CliArgs, ct);
        return;
      }

      var project = target.Project!;
      if (!await preBuildService.BuildBeforeRunAsync(project.ProjectFullPath, project.ProjectName, ct))
      {
        return;
      }

      await StartDebugSessionAsync(project, target.LaunchProfileName, request.CliArgs, ct);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error starting debug session");
      await editorService.DisplayError($"Debug session failed: {ex.Message}");
    }
  }

  public async Task WatchAsync(WorkspaceWatchRequest request, CancellationToken ct)
  {
    if (request.UseDebugger)
    {
      throw new NotImplementedException("Debugger not available for dotnet watch");
    }
    if (!ValidateFilePath(request.FilePath))
    {
      return;
    }

    var target = await resolver.ResolveAsync(request.FilePath, request.UseDefault, request.UseLaunchProfile, "watch", ct);
    if (target is null)
    {
      return;
    }

    ValidatedDotnetProject project;
    string sessionKey;

    if (target.Kind == ExecutionTargetKind.SingleFile)
    {
      var converted = await ConvertSingleFileToProjectAsync(target.SingleFilePath!, ct);
      if (converted is null)
      {
        return;
      }

      project = converted;
      sessionKey = $"watch:{target.SingleFilePath}";
    }
    else
    {
      project = target.Project!;
      sessionKey = $"watch:{project.ProjectFullPath}";
    }

    if (!sessionManager.TryRegister(sessionKey))
    {
      await editorService.DisplayError($"{project.ProjectName} is already being watched");
      return;
    }

    var args = new List<string> { "watch", "--non-interactive" };
    if (target.LaunchProfileName is not null)
    {
      args.AddRange(["--launch-profile", target.LaunchProfileName]);
    }

    if (!string.IsNullOrEmpty(request.CliArgs))
    {
      args.Add("--");
      args.AddRange(CommandLineParser.SplitCommandLine(request.CliArgs));
    }

    var command = new RunCommand(
        "dotnet",
        [.. args],
        Path.GetDirectoryName(project.ProjectFullPath) ?? ".",
        []);

    _ = Task.Run(async () =>
    {
      try { await editorService.RequestRunCommandAsync(command, CancellationToken.None); }
      finally { sessionManager.Unregister(sessionKey); }
    }, CancellationToken.None);
  }

  private async Task DispatchRunAsync(
      ValidatedDotnetProject project,
      EasyDotnet.IDE.Models.LaunchProfile.LaunchProfile? launchProfile,
      string? cliArgs,
      CancellationToken ct)
  {
    var sessionKey = $"{project.ProjectFullPath}:{project.TargetFramework}";
    if (!sessionManager.TryRegister(sessionKey))
    {
      await editorService.DisplayError($"{project.ProjectName} is already running");
      return;
    }

    var additionalArgs = string.IsNullOrEmpty(cliArgs)
        ? null
        : new[] { "--" }.Concat(CommandLineParser.SplitCommandLine(cliArgs)).ToArray();

    var runRequest = new RunProjectRequest(project.Raw, launchProfile, additionalArgs, null);

    _ = Task.Run(async () =>
    {
      try
      {
        if (!await preBuildService.BuildBeforeRunAsync(
                project.ProjectFullPath, project.ProjectName, CancellationToken.None))
        {
          return;
        }

        var exitCode = await editorService.RequestRunProjectAsync(runRequest, CancellationToken.None);
        logger.LogInformation("{ProjectName} exited with code {ExitCode}", project.ProjectName, exitCode);
        if (exitCode != 0)
        {
          await editorService.DisplayError($"{project.ProjectName} exited with code {exitCode}");
        }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Unexpected error while running {ProjectName}", project.ProjectName);
        await editorService.DisplayError($"Failed to run {project.ProjectName}: {ex.Message}");
      }
      finally
      {
        sessionManager.Unregister(sessionKey);
      }
    }, CancellationToken.None);
  }

  private async Task RunSingleFileAsync(string filePath, string? cliArgs, CancellationToken ct)
  {
    filePath = Path.GetFullPath(filePath);
    var fileName = Path.GetFileName(filePath);

    if (!sessionManager.TryRegister(filePath))
    {
      await editorService.DisplayError($"{fileName} is already running");
      return;
    }

    var args = new List<string> { "run", filePath };
    if (!string.IsNullOrEmpty(cliArgs))
    {
      args.Add("--");
      args.AddRange(CommandLineParser.SplitCommandLine(cliArgs));
    }

    var command = new RunCommand(
        "dotnet", [.. args],
        Path.GetDirectoryName(filePath) ?? ".",
        []);

    _ = Task.Run(async () =>
    {
      try { await editorService.RequestRunCommandAsync(command, CancellationToken.None); }
      finally { sessionManager.Unregister(filePath); }
    }, CancellationToken.None);
  }

  private async Task DebugSingleFileAsync(string filePath, string? cliArgs, CancellationToken ct)
  {
    var project = await ConvertSingleFileToProjectAsync(filePath, ct);
    if (project is null)
    {
      return;
    }

    if (!await preBuildService.BuildBeforeRunAsync(project.ProjectFullPath, project.ProjectName, ct))
    {
      return;
    }

    await StartDebugSessionAsync(project, null, cliArgs, ct);
  }

  private async Task<ValidatedDotnetProject?> ConvertSingleFileToProjectAsync(string filePath, CancellationToken ct)
  {
    var convertResponse = await buildHostManager.ConvertFileToProjectAsync(filePath, ct);
    if (!convertResponse.Properties.Success)
    {
      var errorMsg = convertResponse.Properties.Error?.Message ?? "Unknown error";
      await editorService.DisplayError($"Failed to convert {Path.GetFileName(filePath)}: {errorMsg}");
      return null;
    }

    var project = convertResponse.Properties.Project;
    if (project is null)
    {
      await editorService.DisplayError($"Failed to convert {Path.GetFileName(filePath)}: No project returned");
    }

    return project;
  }

  private async Task StartDebugSessionAsync(
      ValidatedDotnetProject project,
      string? launchProfileName,
      string? cliArgs,
      CancellationToken ct)
  {
    var startRequest = new DebuggerStartRequest(
        project.ProjectFullPath, project.TargetFramework, "Debug", launchProfileName);

    var strategy = debugStrategyFactory.CreateRunInTerminalStrategy(launchProfileName, cliArgs);

    var session = await debugOrchestrator.StartClientDebugSessionAsync(
        project.ProjectFullPath, startRequest, strategy, ct);

    await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
    await session.ProcessStarted;
    await Task.Delay(1000, ct);
  }

  private bool ValidateFilePath(string? filePath)
  {
    if (filePath is null)
    {
      return true;
    }

    if (Path.IsPathRooted(filePath) && filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    _ = editorService.DisplayError($"Invalid FilePath '{filePath}': must be an absolute path to a .cs file");
    return false;
  }
}