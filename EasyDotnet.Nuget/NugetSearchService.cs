using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace EasyDotnet.Nuget;

public sealed class NugetSearchService(INugetSettingsProvider settingsProvider, ILogger<NugetSearchService> logger) : INugetSearchService
{
  private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
  private readonly MemoryCache _cache = new(new MemoryCacheOptions());

  public List<PackageSource> GetSources()
  {
    var sourceProvider = new PackageSourceProvider(settingsProvider.GetSettings());
    return [.. sourceProvider.LoadPackageSources()];
  }

  public async Task<IReadOnlyList<NuGetVersion>> GetVersionsAsync(
      string packageId,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null)
  {
    var key = $"versions::{packageId}::{includePrerelease}::{string.Join(",", sourceNames ?? [])}";
    if (_cache.TryGetValue(key, out IReadOnlyList<NuGetVersion>? cached) && cached is not null)
    {
      return cached;
    }

    DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);
    var nugetLogger = NullLogger.Instance;

    using var cache = new SourceCacheContext();

    var sources = (sourceNames is { Count: > 0 }
        ? GetSources().Where(s => sourceNames.Contains(s.Name))
        : GetSources())
        .ToList();

    var versionTasks = sources.Select(async source =>
    {
      try
      {
        var repo = Repository.Factory.GetCoreV3(source.Source);
        var resource = await repo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var versions = await resource.GetAllVersionsAsync(packageId, cache, nugetLogger, cancellationToken);
        return [.. versions.Where(v => includePrerelease || !v.IsPrerelease)];
      }
      catch (Exception e)
      {
        logger.LogError("Failed to get package versions in source {name}: {ex}", source.Name, e);
        return Enumerable.Empty<NuGetVersion>();
      }
    });

    var versionLists = await Task.WhenAll(versionTasks);

    var result = (IReadOnlyList<NuGetVersion>)versionLists
        .SelectMany(v => v)
        .Distinct()
        .OrderByDescending(v => v)
        .ToList();

    _cache.Set(key, result, CacheTtl);
    return result;
  }

  public async Task<IReadOnlyList<NugetPackageHit>> SearchByPrefixAsync(
      string searchTerm,
      int take,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null)
  {
    var key = $"prefix::{searchTerm}::{take}::{includePrerelease}::{string.Join(",", sourceNames ?? [])}";
    if (_cache.TryGetValue(key, out IReadOnlyList<NugetPackageHit>? cached) && cached is not null)
    {
      return cached;
    }

    var raw = await SearchAllSourcesAsync(searchTerm, take, includePrerelease, cancellationToken, sourceNames);
    var hits = (IReadOnlyList<NugetPackageHit>)raw
        .SelectMany(kvp => kvp.Value.Select(m => new NugetPackageHit(
            Source: kvp.Key,
            Id: m.Identity.Id,
            Version: m.Identity.Version.ToString(),
            Description: m.Description,
            DownloadCount: m.DownloadCount,
            Authors: m.Authors)))
        .GroupBy(h => h.Id, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .OrderByDescending(h => h.DownloadCount ?? 0)
        .ToList();

    _cache.Set(key, hits, CacheTtl);
    return hits;
  }

  public async Task<IReadOnlyDictionary<string, IReadOnlyList<IPackageSearchMetadata>>> SearchAllSourcesAsync(
      string searchTerm,
      int take,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null)
  {
    DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);
    var provider = Repository.Provider.GetCoreV3();

    var sourceProvider = new PackageSourceProvider(settingsProvider.GetSettings());
    var allSources = sourceProvider.LoadPackageSources().Where(s => s.IsEnabled);

    var selectedSources = sourceNames is null
        ? allSources
        : allSources.Where(s => sourceNames.Contains(s.Name, StringComparer.OrdinalIgnoreCase));

    var taskMap = selectedSources.ToDictionary(
        source => source.Name,
        async source =>
        {
          try
          {
            var repo = new SourceRepository(source, provider);
            var search = await repo.GetResourceAsync<PackageSearchResource>();

            var results = await search.SearchAsync(
                searchTerm,
                new SearchFilter(includePrerelease),
                skip: 0,
                take: take,
                log: NullLogger.Instance,
                cancellationToken: cancellationToken);

            return (IReadOnlyList<IPackageSearchMetadata>)[.. results];
          }
          catch (Exception e)
          {
            logger.LogError("Failed to search packages in source {name}: {ex}", source.Name, e);
            return (IReadOnlyList<IPackageSearchMetadata>)[];
          }
        });

    await Task.WhenAll(taskMap.Values);

    return taskMap.ToDictionary(
        kvp => kvp.Key,
        kvp => kvp.Value.Result);
  }
}