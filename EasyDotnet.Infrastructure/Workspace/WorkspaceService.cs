using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Workspace;

namespace EasyDotnet.Infrastructure.Workspace;

public class WorkspaceService(IClientService clientService, WorkspaceProjectLoader workspaceProjectLoader, ISolutionService solutionService)
{
  public async Task<List<ProjectEntry>> LazyLoadProjectsAsync(TimeSpan maxWait)
  {
    var rootDir = clientService.ProjectInfo?.RootDir;
    var slnFile = clientService.ProjectInfo?.SolutionFile;

    if (string.IsNullOrEmpty(rootDir)) return [];

    var paths = GetProjectPaths();

    var tasks = paths.Select(path =>
        workspaceProjectLoader.GetOrLoadAsync(path, null, maxWait)
    );

    var results = await Task.WhenAll(tasks);

    return [.. results];
  }

  private string[] GetProjectPaths()
  {
    if (string.IsNullOrEmpty(clientService.ProjectInfo?.RootDir))
      throw new InvalidOperationException("No root dir");

    var sln = clientService.ProjectInfo.SolutionFile;
    if (!string.IsNullOrEmpty(sln) && File.Exists(sln))
    {
      return [.. solutionService.GetProjectsFromSolutionFile(sln).Select(x => x.AbsolutePath)];
    }

    return [.. Directory.GetFiles(clientService.ProjectInfo.RootDir, "*.csproj", SearchOption.AllDirectories).Order()];
  }
}