using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.ProjectView.Models;
using EasyDotnet.IDE.ProjectView.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.ProjectView.Controllers;

public sealed class ProjectViewController(IClientService clientService, ProjectViewService service) : BaseController
{
  [JsonRpcMethod("projectview/get", UseSingleObjectParameterDeserialization = true)]
  public async Task<ProjectViewSnapshot?> GetAsync(ProjectViewGetRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    return await service.OpenAsync(request.ProjectPath, ct);
  }

  [JsonRpcMethod("projectview/addPackage", UseSingleObjectParameterDeserialization = true)]
  public async Task AddPackageAsync(ProjectViewRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.AddPackageAsync(request.ProjectPath, ct);
  }

  [JsonRpcMethod("projectview/removePackage", UseSingleObjectParameterDeserialization = true)]
  public async Task RemovePackageAsync(ProjectViewPackageRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.RemovePackageAsync(request.ProjectPath, request.PackageId, ct);
  }

  [JsonRpcMethod("projectview/updatePackage", UseSingleObjectParameterDeserialization = true)]
  public async Task UpdatePackageAsync(ProjectViewPackageRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.UpdatePackageAsync(request.ProjectPath, request.PackageId, ct);
  }

  [JsonRpcMethod("projectview/upgradePackage", UseSingleObjectParameterDeserialization = true)]
  public async Task UpgradePackageAsync(ProjectViewUpgradeRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.UpgradePackageAsync(request.ProjectPath, request.PackageId, request.Version, ct);
  }

  [JsonRpcMethod("projectview/checkOutdated", UseSingleObjectParameterDeserialization = true)]
  public async Task CheckOutdatedAsync(ProjectViewRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.CheckOutdatedAsync(request.ProjectPath, ct);
  }

  [JsonRpcMethod("projectview/upgradeAllOutdated", UseSingleObjectParameterDeserialization = true)]
  public async Task UpgradeAllOutdatedAsync(ProjectViewRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.UpgradeAllOutdatedAsync(request.ProjectPath, ct);
  }

  [JsonRpcMethod("projectview/addProjectReference", UseSingleObjectParameterDeserialization = true)]
  public async Task AddProjectReferenceAsync(ProjectViewRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.AddProjectReferenceAsync(request.ProjectPath, ct);
  }

  [JsonRpcMethod("projectview/removeProjectReference", UseSingleObjectParameterDeserialization = true)]
  public async Task RemoveProjectReferenceAsync(ProjectViewProjectRefRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.RemoveProjectReferenceAsync(request.ProjectPath, request.TargetPath, ct);
  }

  [JsonRpcMethod("projectview/refresh", UseSingleObjectParameterDeserialization = true)]
  public async Task RefreshAsync(ProjectViewRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.RefreshAsync(request.ProjectPath, ct);
  }
}
