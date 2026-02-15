using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.Solution;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Solution;

public class SolutionController(ISolutionService solutionService) : BaseController
{
  [JsonRpcMethod("solution/list-projects")]
  public async Task<List<SolutionFileProjectResponse>> ListProjects(string solutionFilePath, CancellationToken cancellationToken) => (await solutionService.GetProjectsFromSolutionFile(solutionFilePath, cancellationToken)).ConvertAll(x => new SolutionFileProjectResponse(x.ProjectName, x.AbsolutePath));
}