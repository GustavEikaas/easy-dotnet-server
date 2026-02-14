using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.Extensions;
using EasyDotnet.Infrastructure.Editor;
using EasyDotnet.Infrastructure.Settings;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Workspace;

public sealed record WorkspaceBuildRequest(bool UseTerminal, bool UseDefault);

public class WorkspaceBuildController(
  SettingsService settingsService,
  IClientService clientService,
  IVisualStudioLocator locator,
  IMsBuildService msBuildService,
  IProgressScopeFactory progressScopeFactory,
  IEditorService editorService,
  ISolutionService solutionService) : BaseController
{
  [JsonRpcMethod("workspace/build", UseSingleObjectParameterDeserialization = true)]
  public async Task Build(WorkspaceBuildRequest request, CancellationToken cancellationToken)
  {
    var rootDir = clientService.RequireRootDir();
    var solutionFile = clientService.ProjectInfo?.SolutionFile;
    var projectOrSln = await ResolveProject(solutionFile, rootDir, request.UseDefault);
    if (projectOrSln is null) return;
    if (request.UseTerminal)
    {
      var cmd = (clientService?.ClientOptions?.UseVisualStudio ?? false) ? await locator.GetVisualStudioMSBuildPath() : "dotnet";
      await editorService.RequestRunCommand(new(cmd, ["msbuild", projectOrSln], ".", []));
      return;
    }
    else
    {
      using var progress = progressScopeFactory.Create("Building...", "Building...");
      var result = await msBuildService.RequestBuildAsync(projectOrSln, null, null, null, cancellationToken);
      if (!result.Success)
      {

        await editorService.DisplayError("Build failed");
        await editorService.SetQuickFixList([.. result.Errors.Select(x => new QuickFixItem(x.FilePath, x.LineNumber, x.ColumnNumber, x.Message ?? "ERR", QuickFixItemType.Error))]);
      }
      else
      {
        await editorService.DisplayMessage("Built successfully");
      }

    }

  }

  private async Task<string?> ResolveProject(string? solutionFile, string rootDir, bool useDefault)
  {
    if (solutionFile is null)
    {
      throw new NotImplementedException("Solution file is required for now");
      //TODO: fallback to scan disk for .csproj files relative to clientService.ProjectInfo.RootDir
    }
    else
    {
      var project = useDefault ? settingsService.GetDefaultBuildProject(solutionFile) : null;
      return project ?? await RequestProjectSelection(solutionFile);
    }
  }

  private async Task<string?> RequestProjectSelection(string solutionFile)
  {
    var projects = solutionService.GetProjectsFromSolutionFile(solutionFile);
    var options = projects.Select(x => new SelectionOption(x.AbsolutePath, x.ProjectName)).Concat([solutionFile.FromSolutionFile()]).ToArray();
    var selection = await editorService.RequestSelection("Pick project to build", options, solutionFile);
    if (selection?.Id is not null)
    {
      settingsService.SetDefaultBuildProject(selection.Id);
    }
    return selection?.Id;
  }
}
