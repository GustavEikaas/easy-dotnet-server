using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.Solution;
using EasyDotnet.IDE.BuildHost;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Solution;

public class SolutionController(BuildHostManager buildHostManager) : BaseController
{
  [JsonRpcMethod("solution/list-projects")]
  public async Task<List<SolutionFileProjectResponse>> ListProjects(string solutionFilePath, CancellationToken cancellationToken)
  {
    var projects = await buildHostManager.GetSolutionFileProjectsAsync(new(Path.GetFullPath(solutionFilePath)), cancellationToken);

    return [.. projects.Projects.Select(x => new SolutionFileProjectResponse(x.ProjectName, x.AbsolutePath))];
  }
}