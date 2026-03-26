using System.CommandLine.Parsing;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Controllers.NetCoreDbg;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Workspace.Controllers;
using EasyDotnet.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using LaunchProfile = EasyDotnet.Domain.Models.LaunchProfile.LaunchProfile;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceService(
  SettingsService settingsService,
  ILaunchProfileService launchProfileService,
  IEditorService editorService,
  IClientService clientService,
  WorkspaceBuildHostManager buildHostManager,
  WorkspaceSessionManager sessionManager,
  IDebugOrchestrator debugOrchestrator,
  IDebugStrategyFactory debugStrategyFactory,
  ILogger<WorkspaceService> logger)
{
  public async Task RunAsync(WorkspaceRunRequest request, CancellationToken ct)
  {
    if (request.FilePath is not null)
    {
      if (!Path.IsPathRooted(request.FilePath) || !request.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
      {
        await editorService.DisplayError($"Invalid FilePath '{request.FilePath}': must be an absolute path to a .cs file");
        return;
      }
    }

    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    if (solutionFile is null)
    {
      await RunNoSolutionAsync(request, ct);
      return;
    }

    await RunWithSolutionAsync(solutionFile, request, ct);
  }

  public async Task DebugAsync(WorkspaceDebugRequest request, CancellationToken ct)
  {
    try
    {
      if (request.FilePath is not null)
      {
        if (!Path.IsPathRooted(request.FilePath) || !request.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
          await editorService.DisplayError($"Invalid FilePath '{request.FilePath}': must be an absolute path to a .cs file");
          return;
        }
      }

      var solutionFile = clientService.ProjectInfo?.SolutionFile;
      if (solutionFile is not null)
      {
        await DebugProjectAsync(solutionFile, request, ct);
        return;
      }

      await DebugNoSolutionAsync(request, ct);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error starting debug session");
      await editorService.DisplayError($"Debug session failed: {ex.Message}");
    }
  }

  private const string SingleFileOptionId = "__singlefile__";

  private async Task DebugProjectAsync(string solutionFile, WorkspaceDebugRequest request, CancellationToken ct)
  {
    var runRequest = new WorkspaceRunRequest(request.UseDefault, request.UseLaunchProfile, request.FilePath, null);
    var (project, runAsSingleFile) = await ResolveProjectFromSolutionAsync(solutionFile, runRequest, ct);

    if (runAsSingleFile)
    {
      await DebugSingleFileAsync(request, ct);
      return;
    }

    if (project is null) return;
    await DebugKnownProjectAsync(project, request, ct);
  }

  private async Task DebugKnownProjectAsync(ValidatedDotnetProject project, WorkspaceDebugRequest request, CancellationToken ct)
  {
    string? launchProfileName = null;
    if (request.UseLaunchProfile)
    {
      launchProfileName = await ResolveProfileNameAsync(project, request.UseDefault, ct);
    }

    if (!await BuildBeforeRunAsync(project, project.ProjectName, ct))
    {
      return;
    }

    await StartDebugSessionAsync(project, launchProfileName, ct);
  }

  private async Task DebugSingleFileAsync(WorkspaceDebugRequest request, CancellationToken ct)
  {
    var convertResponse = await buildHostManager.ConvertFileToProjectAsync(request.FilePath!, ct);
    if (!convertResponse.Properties.Success)
    {
      var errorMsg = convertResponse.Properties.Error?.Message ?? "Unknown error";
      await editorService.DisplayError($"Failed to convert {Path.GetFileName(request.FilePath)}: {errorMsg}");
      return;
    }

    var project = convertResponse.Properties.Project;
    if (project is null)
    {
      await editorService.DisplayError($"Failed to convert {Path.GetFileName(request.FilePath)}: No project returned");
      return;
    }

    if (!await BuildBeforeRunAsync(project, project.ProjectName, ct))
      return;

    await StartDebugSessionAsync(project, null, ct);
  }

  private async Task DebugNoSolutionAsync(WorkspaceDebugRequest request, CancellationToken ct)
  {
    var rootDir = clientService.RequireRootDir();

    if (request.FilePath is not null)
    {
      var dir = Path.GetDirectoryName(request.FilePath);
      while (dir is not null)
      {
        var csproj = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
        if (csproj is not null)
        {
          var project = await EvaluateProjectAsync(csproj, ct);
          if (project is not null)
          {
            await DebugKnownProjectAsync(project, request, ct);
            return;
          }
          break;
        }

        if (string.Equals(dir, rootDir, StringComparison.OrdinalIgnoreCase))
          break;

        dir = Path.GetDirectoryName(dir);
      }
    }

    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = 3,
      RecurseSubdirectories = true
    }).ToList();

    var runnableProjects = new List<ValidatedDotnetProject>();
    await foreach (var result in buildHostManager.GetProjectPropertiesBatchAsync(
      new GetProjectPropertiesBatchRequest([.. csprojFiles], null), ct))
    {
      if (result.Success && result.Project?.IsRunnable == true)
        runnableProjects.Add(result.Project);
    }

    if (runnableProjects.Count > 0)
    {
      ValidatedDotnetProject picked;
      if (runnableProjects.Count == 1)
      {
        picked = runnableProjects[0];
      }
      else
      {
        var options = runnableProjects.Select(p => new SelectionOption($"{p.ProjectFullPath}:{p.TargetFramework}", $"{p.ProjectName} ({p.TargetFramework})")).ToArray();
        var selected = await editorService.RequestSelection("Pick project to debug", options);
        if (selected is null) return;
        picked = runnableProjects.First(p => $"{p.ProjectFullPath}:{p.TargetFramework}" == selected.Id);
      }

      await DebugKnownProjectAsync(picked, request, ct);
      return;
    }

    if (request.FilePath is not null)
    {
      await DebugSingleFileAsync(request, ct);
      return;
    }

    await editorService.DisplayError("No runnable projects found");
  }

  private async Task<string?> ResolveProfileNameAsync(ValidatedDotnetProject project, bool useDefault, CancellationToken ct)
  {
    if (useDefault)
    {
      var settings = await settingsService.GetValidatedProjectSettings(project.ProjectFullPath, ct);
      return settings?.LaunchProfile;
    }

    var profiles = launchProfileService.GetLaunchProfiles(project.ProjectFullPath);
    if (profiles is null || profiles.Count == 0) return null;

    var options = profiles.Keys.Select(name => new SelectionOption(name, name)).ToArray();
    var selected = await editorService.RequestSelection("Pick launch profile", options);
    if (selected is null) return null;

    settingsService.SetProjectLaunchProfile(project.ProjectFullPath, selected.Id);
    return selected.Id;
  }

  private async Task StartDebugSessionAsync(
      ValidatedDotnetProject project,
      string? launchProfileName,
      CancellationToken ct)
  {
    var startRequest = new DebuggerStartRequest(
        project.ProjectFullPath,
        project.TargetFramework,
        "Debug",
        launchProfileName);

    var strategy = debugStrategyFactory.CreateRunInTerminalStrategy(launchProfileName);

    var session = await debugOrchestrator.StartClientDebugSessionAsync(
        project.ProjectFullPath,
        startRequest,
        strategy,
        ct);

    await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
    await session.ProcessStarted;
    await Task.Delay(1000, ct);
  }

  private async Task RunWithSolutionAsync(string solutionFile, WorkspaceRunRequest request, CancellationToken ct)
  {
    var (project, runAsSingleFile) = await ResolveProjectFromSolutionAsync(solutionFile, request, ct);

    if (runAsSingleFile)
    {
      await RunSingleFileAsync(request.FilePath!, request.CliArgs, ct);
      return;
    }

    if (project is null) return;

    LaunchProfile? launchProfile = null;
    if (request.UseLaunchProfile)
    {
      launchProfile = await ResolveProfileAsync(project, request.UseDefault, ct);
    }

    await DispatchRunAsync($"{project.ProjectFullPath}:{project.TargetFramework}", project.ProjectName, project, launchProfile, request.CliArgs, ct);
  }

  /// <summary>
  /// Resolves the project to run from the solution file, applying persistence heuristics:
  /// <list type="bullet">
  ///   <item>UseDefault=true: reuse persisted startup project (validate it still exists), fall back to picker if stale.</item>
  ///   <item>UseDefault=false: always show picker and persist the selection.</item>
  /// </list>
  /// </summary>
  private async Task<(ValidatedDotnetProject? Project, bool RunAsSingleFile)> ResolveProjectFromSolutionAsync(
    string solutionFile, WorkspaceRunRequest request, CancellationToken ct)
  {
    if (request.UseDefault)
    {
      var defaultPath = settingsService.GetDefaultStartupProject();
      if (defaultPath is not null && File.Exists(defaultPath))
      {
        var project = await EvaluateProjectAsync(defaultPath, ct);
        if (project is not null) return (project, false);
      }

      settingsService.SetDefaultStartupProject(null);
    }

    return await PickAndPersistProjectAsync(solutionFile, request.FilePath, ct);
  }

  private async Task<(ValidatedDotnetProject? Project, bool RunAsSingleFile)> PickAndPersistProjectAsync(
    string solutionFile, string? singleFilePath, CancellationToken ct)
  {
    var projects = await buildHostManager.GetProjectsFromSolutionAsync(solutionFile, p => p.IsRunnable, ct: ct);

    if (projects.Count == 0 && singleFilePath is null)
    {
      await editorService.DisplayError("No runnable projects found in solution");
      return (null, false);
    }

    if (projects.Count == 1 && singleFilePath is null)
    {
      settingsService.SetDefaultStartupProject(projects[0].ProjectFullPath);
      return (projects[0], false);
    }

    var options = new List<SelectionOption>();
    if (singleFilePath is not null)
      options.Add(new SelectionOption(SingleFileOptionId, $"{Path.GetFileName(singleFilePath)} (script)"));
    options.AddRange(projects.Select(p => new SelectionOption($"{p.ProjectFullPath}:{p.TargetFramework}", $"{p.ProjectName} ({p.TargetFramework})")));

    var selected = await editorService.RequestSelection("Pick project to run", [.. options]);
    if (selected is null) return (null, false);

    if (selected.Id == SingleFileOptionId)
      return (null, true);

    var picked = projects.First(p => $"{p.ProjectFullPath}:{p.TargetFramework}" == selected.Id);
    settingsService.SetDefaultStartupProject(picked.ProjectFullPath);
    return (picked, false);
  }

  private async Task RunNoSolutionAsync(WorkspaceRunRequest request, CancellationToken ct)
  {
    var rootDir = clientService.RequireRootDir();

    // Walk up from the file's directory to find the nearest owning .csproj
    if (request.FilePath is not null)
    {
      var dir = Path.GetDirectoryName(request.FilePath);
      while (dir is not null)
      {
        var csproj = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
        if (csproj is not null)
        {
          var project = await EvaluateProjectAsync(csproj, ct);
          if (project is not null)
          {
            LaunchProfile? launchProfile = null;
            if (request.UseLaunchProfile)
              launchProfile = await PickProfileInteractiveAsync(project, ct);
            await DispatchRunAsync($"{project.ProjectFullPath}:{project.TargetFramework}", project.ProjectName, project, launchProfile, request.CliArgs, ct);
            return;
          }
          break;
        }

        if (string.Equals(dir, rootDir, StringComparison.OrdinalIgnoreCase))
          break;

        dir = Path.GetDirectoryName(dir);
      }
    }

    // Search workspace root for runnable projects
    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = 3,
      RecurseSubdirectories = true
    }).ToList();

    var runnableProjects = new List<ValidatedDotnetProject>();
    await foreach (var result in buildHostManager.GetProjectPropertiesBatchAsync(
      new GetProjectPropertiesBatchRequest([.. csprojFiles], null), ct))
    {
      if (result.Success && result.Project?.IsRunnable == true)
        runnableProjects.Add(result.Project);
    }

    if (runnableProjects.Count > 0)
    {
      ValidatedDotnetProject project;
      if (runnableProjects.Count == 1)
      {
        project = runnableProjects[0];
      }
      else
      {
        var options = runnableProjects
          .Select(p => new SelectionOption($"{p.ProjectFullPath}:{p.TargetFramework}", $"{p.ProjectName} ({p.TargetFramework})"))
          .ToArray();
        var selection = await editorService.RequestSelection("Pick project to run", options);
        if (selection is null) return;
        project = runnableProjects.First(p => $"{p.ProjectFullPath}:{p.TargetFramework}" == selection.Id);
      }

      LaunchProfile? launchProfile = null;
      if (request.UseLaunchProfile)
        launchProfile = await PickProfileInteractiveAsync(project, ct);

      await DispatchRunAsync($"{project.ProjectFullPath}:{project.TargetFramework}", project.ProjectName, project, launchProfile, request.CliArgs, ct);
      return;
    }

    // Last resort: single file
    if (!string.IsNullOrEmpty(request.FilePath))
    {
      await RunSingleFileAsync(request.FilePath, request.CliArgs, ct);
      return;
    }

    await editorService.DisplayError("No runnable projects found");
  }

  /// <summary>
  /// Resolves the launch profile, applying persistence heuristics:
  /// <list type="bullet">
  ///   <item>UseDefault=true: reuse persisted profile (already validated by GetValidatedProjectSettings), run without if cleared.</item>
  ///   <item>UseDefault=false: always show picker and persist the selection. Zero profiles → run without profile.</item>
  /// </list>
  /// </summary>
  private async Task<LaunchProfile?> ResolveProfileAsync(
    ValidatedDotnetProject project, bool useDefault, CancellationToken ct)
  {
    if (useDefault)
    {
      var settings = await settingsService.GetValidatedProjectSettings(project.ProjectFullPath, ct);
      if (settings?.LaunchProfile is not null)
      {
        return launchProfileService.GetLaunchProfile(project.ProjectFullPath, settings.LaunchProfile);
      }
      return null;
    }

    return await PickProfileInteractiveAsync(project, ct);
  }

  private async Task<LaunchProfile?> PickProfileInteractiveAsync(ValidatedDotnetProject project, CancellationToken ct)
  {
    var profiles = launchProfileService.GetLaunchProfiles(project.ProjectFullPath);
    if (profiles is null || profiles.Count == 0) return null;

    var options = profiles.Keys.Select(name => new SelectionOption(name, name)).ToArray();
    var selected = await editorService.RequestSelection("Pick launch profile", options);
    if (selected is null) return null;

    settingsService.SetProjectLaunchProfile(project.ProjectFullPath, selected.Id);
    return profiles[selected.Id];
  }

  private async Task DispatchRunAsync(
    string sessionKey,
    string projectName,
    ValidatedDotnetProject project,
    LaunchProfile? launchProfile,
    string? cliArgs,
    CancellationToken ct)
  {
    if (!sessionManager.TryRegister(sessionKey))
    {
      await editorService.DisplayError($"{projectName} is already running");
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
        var ready = await BuildBeforeRunAsync(project, projectName, CancellationToken.None);
        if (!ready) return;

        var exitCode = await editorService.RequestRunProjectAsync(runRequest, CancellationToken.None);
        logger.LogInformation("{ProjectName} exited with code {ExitCode}", projectName, exitCode);
        if (exitCode != 0)
          await editorService.DisplayError($"{projectName} exited with code {exitCode}");
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Unexpected error while running {ProjectName}", projectName);
        await editorService.DisplayError($"Failed to run {projectName}: {ex.Message}");
      }
      finally
      {
        sessionManager.Unregister(sessionKey);
      }
    }, CancellationToken.None);
  }

  private async Task<bool> BuildBeforeRunAsync(ValidatedDotnetProject project, string projectName, CancellationToken ct)
  {
    var token = Guid.NewGuid().ToString();

    await editorService.SendProgressStart(token, "Restoring...", $"Restoring {projectName}");
    var restoreErrors = new List<QuickFixItem>();
    var restoreOk = true;
    try
    {
      await foreach (var result in buildHostManager.RestoreNugetPackagesAsync(
        new RestoreRequest([project.ProjectFullPath]), ct))
      {
        if (result.Output?.Diagnostics is { } rd)
          restoreErrors.AddRange(MapErrors(rd));
        if (!result.Success)
          restoreOk = false;
      }
    }
    finally
    {
      await editorService.SendProgressEnd(token);
    }

    if (!restoreOk)
    {
      await editorService.SetQuickFixList([.. restoreErrors]);
      await editorService.DisplayError($"Restore failed for {projectName}");
      return false;
    }

    await editorService.SendProgressStart(token, "Building...", $"Building {projectName}");
    var buildErrors = new List<QuickFixItem>();
    var buildOk = true;
    try
    {
      await foreach (var result in buildHostManager.BatchBuildAsync(
        new BatchBuildRequest([project.ProjectFullPath], "Debug"), ct))
      {
        if (result.Output?.Diagnostics is { } bd)
          buildErrors.AddRange(MapErrors(bd));
        if (result.Kind == BatchBuildResultKind.Finished && result.Success == false)
          buildOk = false;
      }
    }
    finally
    {
      await editorService.SendProgressEnd(token);
    }

    if (!buildOk)
    {
      await editorService.SetQuickFixList([.. buildErrors]);
      await editorService.DisplayError($"Build failed for {projectName}");
      return false;
    }

    return true;
  }

  private static IEnumerable<QuickFixItem> MapErrors(BuildDiagnostic[] diagnostics) =>
    diagnostics
      .Where(d => d.Severity == BuildDiagnosticSeverity.Error)
      .Select(d => new QuickFixItem(
        FileName: d.File ?? "",
        LineNumber: d.LineNumber,
        ColumnNumber: d.ColumnNumber,
        Text: string.IsNullOrEmpty(d.Code) ? d.Message ?? "" : $"[{d.Code}] {d.Message}",
        Type: QuickFixItemType.Error));

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
      "dotnet",
      [.. args],
      Path.GetDirectoryName(filePath) ?? ".",
      []);

    _ = Task.Run(async () =>
    {
      try
      {
        await editorService.RequestRunCommandAsync(command, CancellationToken.None);
      }
      finally
      {
        sessionManager.Unregister(filePath);
      }
    }, CancellationToken.None);
  }

  private async Task<ValidatedDotnetProject?> EvaluateProjectAsync(string projectPath, CancellationToken ct)
  {
    await foreach (var result in buildHostManager.GetProjectPropertiesBatchAsync(
      new GetProjectPropertiesBatchRequest([projectPath], null), ct))
    {
      if (result.Success && result.Project is not null) return result.Project;
    }

    await editorService.DisplayError($"Failed to evaluate project {Path.GetFileName(projectPath)}");
    return null;
  }
}