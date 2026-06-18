using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using StreamJsonRpc;

namespace EasyDotnet.IDE.PackageManager;

public sealed class PackageManagerController(IClientService clientService, PackageManagerService service) : BaseController
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
}