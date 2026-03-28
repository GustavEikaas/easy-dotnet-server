using System.CommandLine.Parsing;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Controllers.NetCoreDbg;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Workspace.Controllers;
using EasyDotnet.Infrastructure;
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
  WorkspacePreBuildService preBuildService,
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
        await DebugWithSolutionAsync(solutionFile, request, ct);
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

  private async Task DebugWithSolutionAsync(string solutionFile, WorkspaceDebugRequest request, CancellationToken ct)
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

    if (!await preBuildService.BuildBeforeRunAsync(project.ProjectFullPath, project.ProjectName, ct))
    {
      return;
    }

    await StartDebugSessionAsync(project, launchProfileName, request.CliArgs, ct);
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

    if (!await preBuildService.BuildBeforeRunAsync(project.ProjectFullPath, project.ProjectName, ct))
      return;

    await StartDebugSessionAsync(project, null, request.CliArgs, ct);
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

    var runnableProjects = await GetRunnableProjectsAsync(rootDir, ct);
    if (runnableProjects.Count > 0)
    {
      var picked = await PickRunnableProjectAsync(runnableProjects, "Pick project to debug", ct);
      if (picked is null) return;
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
      string? cliArgs,
      CancellationToken ct)
  {
    var startRequest = new DebuggerStartRequest(
        project.ProjectFullPath,
        project.TargetFramework,
        "Debug",
        launchProfileName);

    var strategy = debugStrategyFactory.CreateRunInTerminalStrategy(launchProfileName, cliArgs);

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
        var storedTfm = await settingsService.GetProjectTargetFramework(defaultPath, ct);
        var project = storedTfm is not null
          ? await buildHostManager.GetProjectAsync(defaultPath, storedTfm, ct: ct)
          : await EvaluateProjectAsync(defaultPath, ct);
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

    var includeScriptOption = singleFilePath is not null && !HasCsprojInParentDirs(singleFilePath);

    if (projects.Count == 0 && !includeScriptOption)
    {
      await editorService.DisplayError("No runnable projects found in solution");
      return (null, false);
    }

    if (projects.Count == 1 && !includeScriptOption)
    {
      settingsService.SetDefaultStartupProject(projects[0].ProjectFullPath);
      settingsService.SetProjectTargetFramework(projects[0].ProjectFullPath, projects[0].TargetFramework);
      return (projects[0], false);
    }

    var options = new List<SelectionOption>();
    if (includeScriptOption)
      options.Add(new SelectionOption(SingleFileOptionId, $"{Path.GetFileName(singleFilePath)} (script)"));
    options.AddRange(projects.Select(p => new SelectionOption($"{p.ProjectFullPath}:{p.TargetFramework}", $"{p.ProjectName} ({p.TargetFramework})")));

    var selected = await editorService.RequestSelection("Pick project to run", [.. options]);
    if (selected is null) return (null, false);

    if (selected.Id == SingleFileOptionId)
      return (null, true);

    var picked = projects.First(p => $"{p.ProjectFullPath}:{p.TargetFramework}" == selected.Id);
    settingsService.SetDefaultStartupProject(picked.ProjectFullPath);
    settingsService.SetProjectTargetFramework(picked.ProjectFullPath, picked.TargetFramework);
    return (picked, false);
  }

  private bool HasCsprojInParentDirs(string filePath)
  {
    var rootDir = clientService.RequireRootDir();
    var dir = Path.GetDirectoryName(filePath);
    while (dir is not null)
    {
      if (Directory.EnumerateFiles(dir, "*.csproj").Any())
        return true;

      if (string.Equals(dir, rootDir, StringComparison.OrdinalIgnoreCase))
        break;

      dir = Path.GetDirectoryName(dir);
    }
    return false;
  }

  private async Task RunNoSolutionAsync(WorkspaceRunRequest request, CancellationToken ct)
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

    var runnableProjects = await GetRunnableProjectsAsync(rootDir, ct);
    if (runnableProjects.Count > 0)
    {
      var project = await PickRunnableProjectAsync(runnableProjects, "Pick project to run", ct);
      if (project is null) return;

      LaunchProfile? launchProfile = null;
      if (request.UseLaunchProfile)
        launchProfile = await PickProfileInteractiveAsync(project, ct);

      await DispatchRunAsync($"{project.ProjectFullPath}:{project.TargetFramework}", project.ProjectName, project, launchProfile, request.CliArgs, ct);
      return;
    }

    if (!string.IsNullOrEmpty(request.FilePath))
    {
      await RunSingleFileAsync(request.FilePath, request.CliArgs, ct);
      return;
    }

    await editorService.DisplayError("No runnable projects found");
  }

  private async Task<List<ValidatedDotnetProject>> GetRunnableProjectsAsync(string rootDir, CancellationToken ct)
  {
    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = 3,
      RecurseSubdirectories = true
    });

    return [.. (await buildHostManager.GetProjectPropertiesBatchAsync(
        new GetProjectPropertiesBatchRequest([.. csprojFiles], null), ct).ToListAsync(ct))
      .Where(r => r.Success && r.Project?.IsRunnable == true)
      .Select(r => r.Project!)];
  }

  private async Task<ValidatedDotnetProject?> PickRunnableProjectAsync(
    List<ValidatedDotnetProject> projects, string prompt, CancellationToken ct)
  {
    if (projects.Count == 1) return projects[0];

    var options = projects.Select(p => new SelectionOption(
      $"{p.ProjectFullPath}:{p.TargetFramework}",
      $"{p.ProjectName} ({p.TargetFramework})")).ToArray();
    var selected = await editorService.RequestSelection(prompt, options);
    return selected is null ? null : projects.First(p => $"{p.ProjectFullPath}:{p.TargetFramework}" == selected.Id);
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
        var ready = await preBuildService.BuildBeforeRunAsync(project.ProjectFullPath, projectName, CancellationToken.None);
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