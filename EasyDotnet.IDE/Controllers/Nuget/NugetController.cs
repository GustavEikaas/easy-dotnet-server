using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Nuget;

public class NugetController(IClientService clientService, NugetService nugetService) : BaseController
{

  [JsonRpcMethod("nuget/restore")]
  public async Task<RestoreResult> RestorePackages(string targetPath)
  {
    clientService.ThrowIfNotInitialized();

    var result = await nugetService.RestorePackagesAsync(targetPath, CancellationToken.None);
    return result;
  }

  [JsonRpcMethod("nuget/list-sources")]
  public IAsyncEnumerable<NugetSourceResponse> GetSources()
  {
    clientService.ThrowIfNotInitialized();

    var sources = nugetService.GetSources();
    return sources.Select(x => x.ToResponse()).ToBatchedAsyncEnumerable(50);
  }

  [JsonRpcMethod("nuget/push")]
  public async Task<NugetPushResponse> PushPackages(List<string> packagePaths, string source, string? apiKey = null)
  {
    clientService.ThrowIfNotInitialized();

    var sources = await nugetService.PushPackageAsync(packagePaths, source, apiKey);
    return new NugetPushResponse(sources);
  }

  [JsonRpcMethod("nuget/get-package-versions")]
  public async Task<IAsyncEnumerable<string>> GetPackageVersions(string packageId, List<string>? sources = null, bool includePrerelease = false)
  {
    clientService.ThrowIfNotInitialized();

    var versions = await nugetService.GetPackageVersionsAsync(
        packageId,
        new CancellationToken(),
        includePrerelease,
        sources);

    return versions.OrderBy(v => v.Version).Select(v => v.ToNormalizedString()).ToBatchedAsyncEnumerable(50);
  }

  [JsonRpcMethod("nuget/search-packages")]
  public async Task<IAsyncEnumerable<NugetPackageMetadata>> SearchPackages(string searchTerm, List<string>? sources = null)
  {
    clientService.ThrowIfNotInitialized();

    var packages = await NugetService.SearchAllSourcesByNameAsync(searchTerm, new CancellationToken(), take: 10, includePrerelease: false, sources);

    var list = packages
        .SelectMany(kvp => kvp.Value.Select(x => NugetPackageMetadata.From(x, kvp.Key)))
        .AsAsyncEnumerable();

    return list;
  }
}