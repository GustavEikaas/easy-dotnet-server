using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.IDE.Controllers.MsBuild;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.MsBuild;

public class MsBuildController(IClientService clientService, MsBuildService msBuild) : BaseController
{
  [JsonRpcMethod("msbuild/build")]
  public async Task<BuildResultResponse> Build(BuildRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var result = await msBuild.RequestBuildAsync(request.TargetPath, request.TargetFramework, request.BuildArgs, request.ConfigurationOrDefault);

    return new(result.Success, result.Errors.AsAsyncEnumerable(), result.Warnings.AsAsyncEnumerable());
  }

  [JsonRpcMethod("msbuild/project-properties")]
  public async Task<DotnetProject> QueryProjectProperties(ProjectPropertiesRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var result = await msBuild.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.ConfigurationOrDefault);
    return result;
  }

  [JsonRpcMethod("msbuild/list-project-reference")]
  public async Task<List<string>> GetProjectReferences(string projectPath)
  {
    clientService.ThrowIfNotInitialized();
    return await msBuild.GetProjectReferencesAsync(Path.GetFullPath(projectPath));
  }

  [JsonRpcMethod("msbuild/add-project-reference")]
  public async Task<bool> AddProjectReference(string projectPath, string targetPath, CancellationToken cancellationToken)
  {
    clientService.ThrowIfNotInitialized();
    return await msBuild.AddProjectReferenceAsync(Path.GetFullPath(projectPath), Path.GetFullPath(targetPath), cancellationToken);
  }

  [JsonRpcMethod("msbuild/remove-project-reference")]
  public async Task<bool> RemoveProjectReference(string projectPath, string targetPath, CancellationToken cancellationToken)
  {
    clientService.ThrowIfNotInitialized();
    return await msBuild.RemoveProjectReferenceAsync(Path.GetFullPath(projectPath), targetPath, cancellationToken);
  }
}