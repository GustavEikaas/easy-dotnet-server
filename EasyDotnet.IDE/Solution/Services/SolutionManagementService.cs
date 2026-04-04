using System.IO.Abstractions;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client.Prompt;

namespace EasyDotnet.IDE.Solution.Services;

public class SolutionManagementService(
    IClientService clientService,
    ISolutionService solutionService,
    IEditorService editorService,
    IFileSystem fileSystem)
{
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