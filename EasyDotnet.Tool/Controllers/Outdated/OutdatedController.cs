using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Outdated;

public class OutdatedController(OutdatedService oudatedService) : BaseController
{

  [JsonRpcMethod("outdated/packages")]
  public async Task<IAsyncEnumerable<OutdatedDependencyInfoResponse>> GetOutdatedPackages(string targetPath, bool? includeTransitive = false)
  {
    var dependencies = await oudatedService.AnalyzeProjectDependenciesAsync(
                        targetPath,
                        includeTransitive: includeTransitive ?? false,
                        includeUpToDate: true
                    );

    return dependencies.Select(x => x.ToResponse()).AsAsyncEnumerable();
  }
}