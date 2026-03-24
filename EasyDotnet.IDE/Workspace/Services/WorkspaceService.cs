using System.CommandLine.Parsing;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.BuildHost;
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

  private async Task RunWithSolutionAsync(string solutionFile, WorkspaceRunRequest request, CancellationToken ct)
  {
    var project = await ResolveProjectFromSolutionAsync(solutionFile, request, ct);
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
  private async Task<ValidatedDotnetProject?> ResolveProjectFromSolutionAsync(
    string solutionFile, WorkspaceRunRequest request, CancellationToken ct)
  {
    if (request.UseDefault)
    {
      var defaultPath = settingsService.GetDefaultStartupProject();
      if (defaultPath is not null && File.Exists(defaultPath))
      {
        var project = await EvaluateProjectAsync(defaultPath, ct);
        if (project is not null) return project;
      }

      settingsService.SetDefaultStartupProject(null);
    }

    return await PickAndPersistProjectAsync(solutionFile, ct);
  }

  private async Task<ValidatedDotnetProject?> PickAndPersistProjectAsync(string solutionFile, CancellationToken ct)
  {
    var projects = await buildHostManager.GetProjectsFromSolutionAsync(solutionFile, p => p.IsRunnable, ct: ct);

    if (projects.Count == 0)
    {
      await editorService.DisplayError("No runnable projects found in solution");
      return null;
    }

    ValidatedDotnetProject picked;
    if (projects.Count == 1)
    {
      picked = projects[0];
    }
    else
    {
      var options = projects.Select(p => new SelectionOption(p.ProjectFullPath, p.ProjectName)).ToArray();
      var selected = await editorService.RequestSelection("Pick project to run", options);
      if (selected is null) return null;
      picked = projects.First(p => p.ProjectFullPath == selected.Id);
    }

    settingsService.SetDefaultStartupProject(picked.ProjectFullPath);
    return picked;
  }

  private async Task RunNoSolutionAsync(WorkspaceRunRequest request, CancellationToken ct)
  {
    var rootDir = clientService.ProjectInfo?.RootDir;
    if (rootDir is null)
    {
      await editorService.DisplayError("No workspace root found");
      return;
    }

    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = 3,
      RecurseSubdirectories = true
    }).ToList();

    if (csprojFiles.Count == 0)
    {
      if (!string.IsNullOrEmpty(request.FilePath))
      {
        await RunSingleFileAsync(request.FilePath, request.CliArgs, ct);
        return;
      }

      await editorService.DisplayError("No runnable projects found");
      return;
    }

    var runnableProjects = new List<ValidatedDotnetProject>();
    await foreach (var result in buildHostManager.GetProjectPropertiesBatchAsync(
      new GetProjectPropertiesBatchRequest([.. csprojFiles], null), ct))
    {
      if (result.Success && result.Project?.IsRunnable == true)
        runnableProjects.Add(result.Project);
    }

    if (runnableProjects.Count == 0)
    {
      if (!string.IsNullOrEmpty(request.FilePath))
      {
        await RunSingleFileAsync(request.FilePath, request.CliArgs, ct);
        return;
      }

      await editorService.DisplayError("No runnable projects found");
      return;
    }

    ValidatedDotnetProject project;
    if (runnableProjects.Count == 1)
    {
      project = runnableProjects[0];
    }
    else
    {
      var options = runnableProjects
        .Select(p => new SelectionOption(p.ProjectFullPath, $"{p.ProjectName} ({p.TargetFramework})"))
        .ToArray();
      var selection = await editorService.RequestSelection("Pick project to run", options);
      if (selection is null) return;
      project = runnableProjects.First(p => p.ProjectFullPath == selection.Id);
    }

    LaunchProfile? launchProfile = null;
    if (request.UseLaunchProfile)
    {
      launchProfile = await PickProfileInteractiveAsync(project, ct);
    }

    await DispatchRunAsync($"{project.ProjectFullPath}:{project.TargetFramework}", project.ProjectName, project, launchProfile, request.CliArgs, ct);
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
        new BatchBuildRequest([project.ProjectFullPath], null, project.TargetFramework), ct))
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