using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Nuget;

public class NugetController(ClientService clientService, NugetService nugetService, OutFileWriterService outFileWriterService) : BaseController
{

  [JsonRpcMethod("nuget/restore")]
  public async Task<RestoreResult> RestorePackages(string targetPath)
  {
    clientService.ThrowIfNotInitialized();

    var result = await nugetService.RestorePackagesAsync(targetPath, CancellationToken.None);
    return result;
  }

  [JsonRpcMethod("nuget/list-sources")]
  public List<NugetSourceResponse> GetSources()
  {
    clientService.ThrowIfNotInitialized();

    var sources = nugetService.GetSources();
    return [.. sources.Select(x => x.ToResponse())];
  }

  [JsonRpcMethod("nuget/push")]
  public async Task<NugetPushResponse> PushPackages(List<string> packagePaths, string source, string? apiKey = null)
  {
    clientService.ThrowIfNotInitialized();

    var sources = await nugetService.PushPackageAsync(packagePaths, source, apiKey);
    return new NugetPushResponse(sources);
  }

  [JsonRpcMethod("nuget/get-package-versions")]
  public async Task<List<string>> GetPackageVersions(string packageId, List<string>? sources = null, bool includePrerelease = false)
  {
    clientService.ThrowIfNotInitialized();

    var versions = await nugetService.GetPackageVersionsAsync(
        packageId,
        new CancellationToken(),
        includePrerelease,
        sources);

    return [.. versions.Select(v => v.ToNormalizedString())];
  }

  [JsonRpcMethod("nuget/search-packages")]
  public async Task<FileResultResponse> SearchPackages(string searchTerm, List<string>? sources = null)
  {
    clientService.ThrowIfNotInitialized();

    var packages = await NugetService.SearchAllSourcesByNameAsync(searchTerm, new CancellationToken(), take: 10, includePrerelease: false, sources);

    var list = packages
        .SelectMany(kvp => kvp.Value.Select(x => NugetPackageMetadata.From(x, kvp.Key)))
        .ToList();

    var outFile = Path.GetTempFileName();
    outFileWriterService.WriteNugetResults(list, outFile);

    return new FileResultResponse(outFile);
  }
}