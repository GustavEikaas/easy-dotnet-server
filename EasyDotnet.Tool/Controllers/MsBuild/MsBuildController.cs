using System.Threading.Tasks;
using EasyDotnet.MsBuild.Contracts;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.MsBuild;

public class MsBuildController(ClientService clientService, MsBuildService msBuild) : BaseController
{
  [JsonRpcMethod("msbuild/build")]
  public async Task<BuildResultResponse> Build(BuildRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var result = await msBuild.RequestBuildAsync(request.TargetPath, request.ConfigurationOrDefault);

    return new(result.Success);
  }

  [JsonRpcMethod("msbuild/project-properties")]
  public async Task<DotnetProjectProperties> QueryProperties(QueryProjectPropertiesRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var result = await msBuild.QueryProjectProperties(request.TargetPath, request.ConfigurationOrDefault, request.TargetFramework);

    return result;
  }

}