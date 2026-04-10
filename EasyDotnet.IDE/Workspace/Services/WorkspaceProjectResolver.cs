using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client.Prompt;
using EasyDotnet.IDE.Settings;
using LaunchProfile = EasyDotnet.IDE.Models.LaunchProfile.LaunchProfile;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceProjectResolver(
    SettingsService settingsService,
    ILaunchProfileService launchProfileService,
    IEditorService editorService,
    IClientService clientService,
    WorkspaceBuildHostManager buildHostManager)
{
  private const string SingleFileOptionId = "__singlefile__";
  private const int ProjectSearchDepth = 3;

  public async Task<ResolvedExecutionTarget?> ResolveAsync(
      string? filePath,
      bool useDefault,
      bool useLaunchProfile,
      string operationLabel,
      CancellationToken ct)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;
    var target = solutionFile is not null
        ? await ResolveFromSolutionAsync(solutionFile, filePath, useDefault, operationLabel, ct)
        : await ResolveNoSolutionAsync(filePath, operationLabel, ct);

    if (target is null) return null;
    if (!useLaunchProfile || target.Kind == ExecutionTargetKind.SingleFile) return target;

    var (profileName, profile) = await ResolveProfileAsync(target.Project!, useDefault, ct);
    return target with { LaunchProfileName = profileName, LaunchProfile = profile };
  }

  private async Task<ResolvedExecutionTarget?> ResolveFromSolutionAsync(string solutionFile, string? filePath, bool useDefault, string operationLabel, CancellationToken ct)
  {
    if (useDefault)
    {
      var defaultPath = settingsService.GetDefaultStartupProject();
      if (defaultPath is not null && File.Exists(defaultPath))
      {
        var storedTfm = await settingsService.GetProjectTargetFramework(defaultPath, ct);
        var project = storedTfm is not null
            ? await buildHostManager.GetProjectAsync(defaultPath, storedTfm, ct: ct)
            : await EvaluateProjectAsync(defaultPath, ct);

        if (project is not null) return ProjectTarget(project);
      }
      settingsService.SetDefaultStartupProject(null);
    }

    return await PickAndPersistFromSolutionAsync(solutionFile, filePath, operationLabel, ct);
  }

  private async Task<ResolvedExecutionTarget?> PickAndPersistFromSolutionAsync(
      string solutionFile, string? singleFilePath, string operationLabel, CancellationToken ct)
  {
    var projects = await buildHostManager.GetProjectsFromSolutionAsync(
        solutionFile, p => p.IsRunnable, ct: ct);

    var includeScriptOption = singleFilePath is not null
        && FindCsprojForFile(singleFilePath) is null;

    if (projects.Count == 0 && !includeScriptOption)
    {
      await editorService.DisplayError("No runnable projects found in solution");
      return null;
    }

    if (projects.Count == 1 && !includeScriptOption)
    {
      PersistProject(projects[0]);
      return ProjectTarget(projects[0]);
    }

    var options = new List<SelectionOption>();
    if (includeScriptOption)
      options.Add(new SelectionOption(SingleFileOptionId, $"{Path.GetFileName(singleFilePath)} (script)"));
    options.AddRange(projects.Select(p => new SelectionOption(ProjectKey(p), $"{p.ProjectName} ({p.TargetFramework})")));

    var selected = await editorService.RequestSelection($"Pick project to {operationLabel}", [.. options]);
    if (selected is null) return null;
    if (selected.Id == SingleFileOptionId) return SingleFileTarget(singleFilePath!);

    var picked = projects.First(p => ProjectKey(p) == selected.Id);
    PersistProject(picked);
    return ProjectTarget(picked);
  }

  private async Task<ResolvedExecutionTarget?> ResolveNoSolutionAsync(
      string? filePath, string operationLabel, CancellationToken ct)
  {
    var rootDir = clientService.RequireRootDir();

    if (filePath is not null)
    {
      var csproj = FindCsprojForFile(filePath);
      if (csproj is not null)
      {
        var project = await EvaluateProjectAsync(csproj, ct);
        if (project is not null) return ProjectTarget(project);
        return null;
      }
    }

    var runnableProjects = await GetRunnableProjectsAsync(rootDir, ct);
    if (runnableProjects.Count > 0)
    {
      var picked = await PickProjectAsync(runnableProjects, $"Pick project to {operationLabel}", ct);
      return picked is null ? null : ProjectTarget(picked);
    }

    if (filePath is not null) return SingleFileTarget(filePath);

    await editorService.DisplayError("No runnable projects found");
    return null;
  }

  private async Task<(string? Name, LaunchProfile? Profile)> ResolveProfileAsync(
      ValidatedDotnetProject project, bool useDefault, CancellationToken ct)
  {
    if (useDefault)
    {
      var settings = await settingsService.GetValidatedProjectSettings(project.ProjectFullPath, ct);
      if (settings?.LaunchProfile is not null)
      {
        var profile = launchProfileService.GetLaunchProfile(project.ProjectFullPath, settings.LaunchProfile);
        return (settings.LaunchProfile, profile);
      }
    }

    var profiles = launchProfileService.GetLaunchProfiles(project.ProjectFullPath);
    if (profiles is null || profiles.Count == 0) return (null, null);

    var options = profiles.Keys.Select(name => new SelectionOption(name, name)).ToArray();
    var selected = await editorService.RequestSelection("Pick launch profile", options);
    if (selected is null) return (null, null);

    settingsService.SetProjectLaunchProfile(project.ProjectFullPath, selected.Id);
    return (selected.Id, profiles[selected.Id]);
  }

  private async Task<List<ValidatedDotnetProject>> GetRunnableProjectsAsync(string rootDir, CancellationToken ct)
  {
    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = ProjectSearchDepth,
      RecurseSubdirectories = true
    });

    return [.. (await buildHostManager
            .GetProjectPropertiesBatchAsync(new GetProjectPropertiesBatchRequest([.. csprojFiles], null), ct)
            .ToListAsync(ct))
            .Where(r => r.Success && r.Project?.IsRunnable == true)
            .Select(r => r.Project!)];
  }

  private async Task<ValidatedDotnetProject?> PickProjectAsync(
      List<ValidatedDotnetProject> projects, string prompt, CancellationToken ct)
  {
    if (projects.Count == 1) return projects[0];

    var options = projects
        .Select(p => new SelectionOption(ProjectKey(p), $"{p.ProjectName} ({p.TargetFramework})"))
        .ToArray();

    var selected = await editorService.RequestSelection(prompt, options);
    return selected is null
        ? null
        : projects.First(p => ProjectKey(p) == selected.Id);
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

  /// <summary>
  /// Walks parent directories from <paramref name="filePath"/> up to the workspace root,
  /// returning the first .csproj found, or null if none exists in the hierarchy.
  /// Replaces both the inline walk in ResolveNoSolutionAsync and the old HasCsprojInParentDirs check.
  /// </summary>
  private string? FindCsprojForFile(string filePath)
  {
    var rootDir = clientService.RequireRootDir();
    var dir = Path.GetDirectoryName(filePath);
    while (dir is not null)
    {
      var csproj = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
      if (csproj is not null) return csproj;
      if (string.Equals(dir, rootDir, StringComparison.OrdinalIgnoreCase)) break;
      dir = Path.GetDirectoryName(dir);
    }
    return null;
  }

  private void PersistProject(ValidatedDotnetProject p)
  {
    settingsService.SetDefaultStartupProject(p.ProjectFullPath);
    settingsService.SetProjectTargetFramework(p.ProjectFullPath, p.TargetFramework);
  }

  private static string ProjectKey(ValidatedDotnetProject p) => $"{p.ProjectFullPath}:{p.TargetFramework}";

  private static ResolvedExecutionTarget ProjectTarget(ValidatedDotnetProject p) =>
      new() { Kind = ExecutionTargetKind.Project, Project = p };

  private static ResolvedExecutionTarget SingleFileTarget(string path) =>
      new() { Kind = ExecutionTargetKind.SingleFile, SingleFilePath = path };
}