using System.Threading.Tasks;
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

  [JsonRpcMethod("msbuild/query-properties")]
  public async Task<dynamic> Properties(BuildRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var result = await msBuild.PropertiesAsync(request.TargetPath, request.ConfigurationOrDefault);

    return result;
  }
}