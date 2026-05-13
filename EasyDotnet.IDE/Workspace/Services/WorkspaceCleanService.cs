using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client.Prompt;
using EasyDotnet.IDE.Workspace.Controllers;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceCleanService(
    IClientService clientService,
    ISolutionService solutionService,
    WorkspaceBuildHostManager buildHostManager,
    WorkspaceBuildService buildService,
    IEditorService editorService)
{
  private const string DefaultConfiguration = "Debug";

  public async Task CleanAsync(WorkspaceCleanRequest request, CancellationToken ct)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    if (solutionFile is not null)
    {
      await CleanWithSolutionAsync(solutionFile, ct);
      return;
    }

    await CleanNoSolutionAsync(ct);
  }

  private async Task CleanWithSolutionAsync(string solutionFile, CancellationToken ct)
  {
    var projects = await solutionService.GetProjectsFromSolutionFile(solutionFile, ct);

    var options = new List<SelectionOption>
    {
      new(solutionFile, "Solution")
    };
    options.AddRange(projects.Select(p => new SelectionOption(p.AbsolutePath, p.ProjectName)));

    var selected = await editorService.RequestSelection("Pick project to clean", [.. options]);
    if (selected is null) return;

    await buildService.BuildQuickfixAsync(
        selected.Id,
        Path.GetFileName(selected.Id),
        DefaultConfiguration,
        buildTarget: "Clean",
        operationName: "Clean",
        platform: null,
        ct,
        restoreBeforeOperation: false);
  }

  private async Task CleanNoSolutionAsync(CancellationToken ct)
  {
    var projects = await buildHostManager.GetProjectsFromDirectoryAsync(
        clientService.RequireRootDir(),
        maxDepth: 3,
        ct: ct);

    if (projects.Count == 0)
    {
      await editorService.DisplayError("No project files found");
      return;
    }

    if (projects.Count == 1)
    {
      await buildService.BuildQuickfixAsync(
          projects[0].ProjectFullPath,
          Path.GetFileName(projects[0].ProjectFullPath),
          DefaultConfiguration,
          buildTarget: "Clean",
          operationName: "Clean",
          platform: null,
          ct,
          restoreBeforeOperation: false);
      return;
    }

    var options = projects
        .Select(p => new SelectionOption(p.ProjectFullPath, p.ProjectName))
        .ToArray();

    var selected = await editorService.RequestSelection("Pick project to clean", options);
    if (selected is null) return;

    await buildService.BuildQuickfixAsync(
        selected.Id,
        Path.GetFileName(selected.Id),
        DefaultConfiguration,
        buildTarget: "Clean",
        operationName: "Clean",
        platform: null,
        ct,
        restoreBeforeOperation: false);
  }

}