using System.Collections.Generic;
using System.Linq;
using EasyDotnet.Application.Interfaces;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Solution;

public class SolutionController(ISolutionService solutionService) : BaseController
{
  [JsonRpcMethod("solution/list-projects")]
  public List<SolutionFileProjectResponse> ListProjects(string solutionFilePath) => [.. solutionService.GetProjectsFromSolutionFile(solutionFilePath).Select(x => new SolutionFileProjectResponse(x.ProjectName, x.AbsolutePath))];
}