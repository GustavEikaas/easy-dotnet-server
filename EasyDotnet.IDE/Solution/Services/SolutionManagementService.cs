using System.IO.Abstractions;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client.Prompt;
using EasyDotnet.IDE.Workspace.BuildConfiguration;

namespace EasyDotnet.IDE.Solution.Services;

public class SolutionManagementService(
    IClientService clientService,
    ISolutionService solutionService,
    IEditorService editorService,
    IWorkspaceBuildConfigurationService buildConfigurationService,
    IFileSystem fileSystem)
{
  public async Task SetBuildConfigurationInteractiveAsync(string? buildType, string? platform, CancellationToken ct)
  {
    clientService.RequireSolutionFile();

    var available = await buildConfigurationService.GetAvailableConfigurationsAsync(ct);
    if (available.Count == 0)
    {
      await editorService.DisplayWarning("No build configurations found in solution");
      return;
    }

    var selected = buildType is null
        ? await PromptForConfigurationAsync(available, ct)
        : Resolve(available, buildType, platform);

    if (selected is null)
    {
      if (buildType is not null)
      {
        await editorService.DisplayWarning($"Unknown build configuration '{buildType}'");
      }
      return;
    }

    await buildConfigurationService.SetActiveConfigurationAsync(selected, ct);
    await editorService.DisplayMessage($"Build configuration: {WorkspaceBuildConfigurationDisplay.ToDisplayName(selected, available)}");
  }

  private async Task<WorkspaceBuildConfiguration?> PromptForConfigurationAsync(
      IReadOnlyList<WorkspaceBuildConfiguration> available,
      CancellationToken ct)
  {
    var active = await buildConfigurationService.GetActiveConfigurationAsync(ct);

    var options = available
        .Select(x => new SelectionOption(
            Id: x.DisplayName,
            Display: WorkspaceBuildConfigurationDisplay.ToDisplayName(x, available)))
        .ToArray();

    var selection = await editorService.RequestSelection(
        "Select build configuration",
        options,
        defaultSelectionId: active.DisplayName);

    return selection is null
        ? null
        : available.FirstOrDefault(x => string.Equals(x.DisplayName, selection.Id, StringComparison.Ordinal));
  }

  private static WorkspaceBuildConfiguration? Resolve(
      IReadOnlyList<WorkspaceBuildConfiguration> available,
      string buildType,
      string? platform)
  {
    var match = available.FirstOrDefault(x => string.Equals(x.DisplayName, buildType, StringComparison.OrdinalIgnoreCase));
    if (match is not null && platform is null)
    {
      return match;
    }

    return available.FirstOrDefault(x =>
        string.Equals(x.BuildType, buildType, StringComparison.OrdinalIgnoreCase)
        && (platform is null || string.Equals(x.Platform, platform, StringComparison.OrdinalIgnoreCase)))
        ?? available.FirstOrDefault(x => string.Equals(x.BuildType, buildType, StringComparison.OrdinalIgnoreCase));
  }

  public async Task AddProjectInteractiveAsync(CancellationToken ct)
  {
    var solutionFilePath = clientService.RequireSolutionFile();
    var rootDir = clientService.RequireRootDir();
    var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionFilePath))
        ?? throw new InvalidOperationException("Solution directory cannot be null");

    var existingProjects = await solutionService.GetProjectsFromSolutionFile(solutionFilePath, ct);
    var existingPaths = existingProjects
        .Select(p => Path.GetFullPath(p.AbsolutePath))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var candidates = fileSystem.Directory
        .EnumerateFiles(rootDir, "*.*proj", SearchOption.AllDirectories)
        .Where(f => (f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                 || f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)) && !existingPaths.Contains(Path.GetFullPath(f)))
        .Order()
        .ToList();

    if (candidates.Count == 0)
    {
      await editorService.DisplayWarning("No new projects found to add");
      return;
    }

    var options = candidates
        .Select(f => new SelectionOption(
            Id: Path.GetFullPath(f),
            Display: Path.GetRelativePath(solutionDirectory, f)))
        .ToArray();

    var selection = await editorService.RequestSelection("Select project to add", options);
    if (selection is null) return;

    await solutionService.AddProjectToSolutionAsync(solutionFilePath, selection.Id, ct);
    await editorService.DisplayMessage($"Project '{Path.GetFileName(selection.Id)}' added to solution");
  }

  public async Task RemoveProjectInteractiveAsync(CancellationToken ct)
  {
    var solutionFilePath = clientService.RequireSolutionFile();

    var projects = await solutionService.GetProjectsFromSolutionFile(solutionFilePath, ct);
    if (projects.Count == 0)
    {
      await editorService.DisplayWarning("No projects found in solution");
      return;
    }

    var options = projects
        .Select(p =>
        {
          var exists = fileSystem.File.Exists(p.AbsolutePath);
          var display = exists ? p.ProjectName : $"{p.ProjectName} (not found)";
          return (Option: new SelectionOption(Id: p.AbsolutePath, Display: display), Exists: exists);
        })
        .OrderBy(x => x.Exists)
        .ThenBy(x => x.Option.Display)
        .Select(x => x.Option)
        .ToArray();

    var selection = await editorService.RequestSelection("Select project to remove", options);
    if (selection is null) return;

    await solutionService.RemoveProjectFromSolutionAsync(solutionFilePath, selection.Id, ct);
    await editorService.DisplayMessage($"Project '{Path.GetFileName(selection.Id)}' removed from solution");
  }
}