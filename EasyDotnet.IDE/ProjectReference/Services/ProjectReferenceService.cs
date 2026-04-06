using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client.Prompt;

namespace EasyDotnet.IDE.ProjectReference.Services;

public class ProjectReferenceService(
    IClientService clientService,
    ISolutionService solutionService,
    IMsBuildService msBuildService,
    IEditorService editorService)
{
  public async Task AddProjectReferenceInteractiveAsync(string projectPath, CancellationToken ct)
  {
    var fullProjectPath = Path.GetFullPath(projectPath);
    var solutionFilePath = clientService.RequireSolutionFile();

    var existingRefs = await msBuildService.GetProjectReferencesAsync(fullProjectPath, ct);
    var existingRefPaths = existingRefs
        .Select(Path.GetFullPath)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var solutionProjects = await solutionService.GetProjectsFromSolutionFile(solutionFilePath, ct);

    var candidates = solutionProjects
        .Where(p => !string.Equals(Path.GetFullPath(p.AbsolutePath), fullProjectPath, StringComparison.OrdinalIgnoreCase)
                 && !existingRefPaths.Contains(Path.GetFullPath(p.AbsolutePath)))
        .OrderBy(p => p.ProjectName)
        .ToList();

    if (candidates.Count == 0)
    {
      await editorService.DisplayWarning("No projects available to add as reference");
      return;
    }

    var options = candidates
        .Select(p => new SelectionOption(Id: p.AbsolutePath, Display: p.ProjectName))
        .ToArray();

    var selection = await editorService.RequestSelection("Select project to add as reference", options);
    if (selection is null) return;

    await msBuildService.AddProjectReferenceAsync(fullProjectPath, selection.Id, ct);
    await editorService.DisplayMessage($"Reference to '{Path.GetFileNameWithoutExtension(selection.Id)}' added");
  }

  public async Task RemoveProjectReferenceInteractiveAsync(string projectPath, CancellationToken ct)
  {
    var fullProjectPath = Path.GetFullPath(projectPath);

    var refs = await msBuildService.GetProjectReferencesAsync(fullProjectPath, ct);
    if (refs.Count == 0)
    {
      await editorService.DisplayWarning("No project references found");
      return;
    }

    var options = refs
        .OrderBy(Path.GetFileNameWithoutExtension)
        .Select(r => new SelectionOption(Id: r, Display: Path.GetFileNameWithoutExtension(r) ?? r))
        .ToArray();

    var selection = await editorService.RequestSelection("Select project reference to remove", options);
    if (selection is null) return;

    await msBuildService.RemoveProjectReferenceAsync(fullProjectPath, selection.Id, ct);
    await editorService.DisplayMessage($"Reference to '{Path.GetFileNameWithoutExtension(selection.Id)}' removed");
  }
}