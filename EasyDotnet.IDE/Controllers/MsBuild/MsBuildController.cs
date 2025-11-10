using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.MsBuild;
using EasyDotnet.Services;
using StreamJsonRpc;
using static EasyDotnet.Services.NugetService;

namespace EasyDotnet.IDE.Controllers.MsBuild;

public class MsBuildController(IClientService clientService, IMsBuildService msBuild, NugetService nugetService) : BaseController
{
  [JsonRpcMethod("msbuild/build")]
  public async Task<BuildResultResponse> Build(BuildRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var result = await msBuild.RequestBuildAsync(request.TargetPath, request.TargetFramework, request.BuildArgs, request.ConfigurationOrDefault);

    return new(result.Success, result.Errors.AsAsyncEnumerable(), result.Warnings.AsAsyncEnumerable());
  }

  [JsonRpcMethod("msbuild/project-properties")]
  public async Task<DotnetProjectV1> QueryProjectProperties(ProjectPropertiesRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var project = await msBuild.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.ConfigurationOrDefault);

    return project.ToResponse(await msBuild.BuildRunCommand(project), await msBuild.BuildBuildCommand(project), await msBuild.BuildTestCommand(project));
  }

  [JsonRpcMethod("msbuild/references")]
  public async Task<Dictionary<string, TfmReferencesApiModel>> GetDotnetProjectReferences(string projectPath)
  {
    clientService.ThrowIfNotInitialized();
    var project = await msBuild.GetOrSetProjectPropertiesAsync(projectPath);
    var references = nugetService.GetDotnetProjectReferences(project);

    return references.ToDictionary(
            kvp => kvp.Key,
            kvp => new TfmReferencesApiModel(
                Packages: kvp.Value.Packages
                            .Select(p => new PackageReferenceInfoApiModel(p.Name, p.Version))
                            .ToBatchedAsyncEnumerable(50),
                Projects: kvp.Value.Projects
                            .Select(p => new ProjectReferenceInfoApiModel(p.Name, p.Path))
                            .ToBatchedAsyncEnumerable(50)
            )
        );
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

  public sealed record PackageReferenceInfoApiModel(string Name, string? Version);

  public sealed record ProjectReferenceInfoApiModel(string Name, string? Path);

  public sealed record TfmReferencesApiModel(
      IAsyncEnumerable<PackageReferenceInfoApiModel> Packages,
      IAsyncEnumerable<ProjectReferenceInfoApiModel> Projects
  );
}