using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Utils;
using EasyDotnet.IDE.Workspace.Controllers;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceRestoreService(IClientService clientService, IEditorService editorService)
{

  public async Task RestoreAsync(WorkspaceRestoreRequest request, CancellationToken ct)
  {
    var target = ResolveTarget();
    if (target == null)
    {
      await editorService.DisplayError("No sln or csproj target found");
      return;
    }
    var args = new List<string> { "restore", target };
    var restoreArgs = PassthroughArgs.Split(request.RestoreArgs);

    if (!string.IsNullOrWhiteSpace(request.Configuration) && !PassthroughArgs.SpecifiesConfiguration(restoreArgs))
    {
      args.Add($"-p:Configuration={request.Configuration}");
    }
    args.AddRange(restoreArgs);

    await editorService.RequestRunCommandAsync(new("dotnet", args, clientService.RequireRootDir(), []));
  }

  private string? ResolveTarget()
  {
    var rootDir = clientService.RequireRootDir();

    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    if (solutionFile is not null)
    {
      return solutionFile;
    }

    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = 3,
      RecurseSubdirectories = true,
    }).ToList();

    return csprojFiles.FirstOrDefault();
  }
}