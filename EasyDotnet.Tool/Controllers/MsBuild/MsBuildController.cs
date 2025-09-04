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
    var result = await msBuild.RequestBuildAsync(request.TargetPath, request.TargetFramework, request.BuildArgs, request.ConfigurationOrDefault);

    return new(result.Success, result.Errors.AsAsyncEnumerable(), result.Warnings.AsAsyncEnumerable());
  }

  [JsonRpcMethod("msbuild/project-properties")]
  public async Task<DotnetProjectProperties> QueryProjectProperties(ProjectPropertiesRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var result = await msBuild.GetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.ConfigurationOrDefault);

    return result;
  }
}