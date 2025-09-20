using System.Collections.Generic;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Solution;

public class SolutionController(SolutionService solutionService) : BaseController
{
  [JsonRpcMethod("solution/list-projects")]
  public List<SolutionFileProjectResponse> ListProjects(string solutionFilePath) => solutionService.GetProjectsFromSolutionFile(solutionFilePath);
}