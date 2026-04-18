using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using StreamJsonRpc;

namespace EasyDotnet.IDE.PackageManager;

public sealed class PackageManagerController(IClientService clientService, PackageManagerService service, IBuildHostManager buildHostManager) : BaseController
{
  [JsonRpcMethod("nuget/add-package", UseSingleObjectParameterDeserialization = true)]
  public async Task AddPackageAsync(AddPackageRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.AddPackageAsync(request, ct);
  }

  [JsonRpcMethod("nuget/remove-package", UseSingleObjectParameterDeserialization = true)]
  public async Task RemovePackageAsync(RemovePackageRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    await service.RemovePackageAsync(request, ct);
  }

  [JsonRpcMethod("nuget/list-installed", UseSingleObjectParameterDeserialization = true)]
  public async Task<InstalledPackageReference[]> ListInstalledAsync(ListInstalledRequest request, CancellationToken ct)
  {
    clientService.ThrowIfNotInitialized();
    return await buildHostManager.ListPackageReferencesAsync(request.ProjectPath, ct);
  }
}