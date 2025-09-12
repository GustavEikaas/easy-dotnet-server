using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyDotnet.Services;
using Microsoft.CodeAnalysis.MSBuild;
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
    var result = await msBuild.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.ConfigurationOrDefault);

    return result;
  }

  [JsonRpcMethod("msbuild/project-references")]
  public async Task<List<string>> GetProjectReferences(string targetPath)
  {
    clientService.ThrowIfNotInitialized();
    using var workspace = MSBuildWorkspace.Create();
    var project = await workspace.OpenProjectAsync(targetPath);
    return project.ProjectReferences
            .Select(r => workspace.CurrentSolution.GetProject(r.ProjectId)?.FilePath)
            .Where(path => path != null)
            .ToList()!;
  }
}