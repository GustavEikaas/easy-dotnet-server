using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Nuget;

public class NugetController(IClientService clientService, NugetService nugetService) : BaseController
{
  [JsonRpcMethod("nuget/get-package-versions")]
  public async Task<IAsyncEnumerable<string>> GetPackageVersions(string packageId, List<string>? sources = null, bool includePrerelease = false)
  {
    clientService.ThrowIfNotInitialized();

    var versions = await nugetService.GetPackageVersionsAsync(
        packageId,
        new CancellationToken(),
        includePrerelease,
        sources);

    return versions.OrderByDescending(v => v.Version).Select(v => v.ToNormalizedString()).ToBatchedAsyncEnumerable(50);
  }

  [JsonRpcMethod("nuget/search-packages")]
  public async Task<IAsyncEnumerable<NugetPackageMetadata>> SearchPackages(string searchTerm, List<string>? sources = null)
  {
    clientService.ThrowIfNotInitialized();

    var packages = await nugetService.SearchAllSourcesByNameAsync(searchTerm, new CancellationToken(), take: 10, includePrerelease: false, sources);

    return packages
        .SelectMany(kvp => kvp.Value.Select(x => NugetPackageMetadata.From(x, kvp.Key)))
        .AsAsyncEnumerable();
  }
}