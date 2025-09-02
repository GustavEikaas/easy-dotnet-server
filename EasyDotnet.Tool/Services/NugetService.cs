using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using StreamJsonRpc;

namespace EasyDotnet.Services;

public sealed record RestoreResult(bool Success, IAsyncEnumerable<string> Errors, IAsyncEnumerable<string> Warnings);

public class NugetService(ClientService clientService, LogService logger)
{

  private static (string Command, string Arguments) GetCommandAndArguments(
      MSBuildType type,
      string targetPath) => type switch
      {
        MSBuildType.SDK => ("dotnet", $"restore \"{targetPath}\" "),
        MSBuildType.VisualStudio => ("nuget", $"restore \"{targetPath}\""),
        _ => throw new InvalidOperationException("Unknown MSBuild type")
      };

  public async Task<RestoreResult> RestorePackagesAsync(string targetPath, CancellationToken cancellationToken)
  {
    var (command, args) = GetCommandAndArguments(clientService.UseVisualStudio ? MSBuildType.VisualStudio : MSBuildType.SDK, targetPath);
    logger.Info($"Starting restore `{command} {args}`");
    var (success, stdout, stderr) = await ProcessUtils.RunProcessAsync(command, args, cancellationToken);

    var errors = stderr
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
        .ToList();

    var warnings = (stdout + Environment.NewLine + stderr)
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Where(l => l.Contains("warning", StringComparison.OrdinalIgnoreCase))
        .AsAsyncEnumerable();

    return new RestoreResult(success && errors.Count == 0, errors.AsAsyncEnumerable(), warnings);
  }

  public List<PackageSource> GetSources()
  {
    var settings = Settings.LoadDefaultSettings(root: null);
    var sourceProvider = new PackageSourceProvider(settings);
    var sources = sourceProvider.LoadPackageSources();
    return [.. sources];
  }

  public async Task<IEnumerable<NuGetVersion>> GetPackageVersionsAsync(
      string packageId,
      CancellationToken cancellationToken,
      bool includePrerelease = false,
      List<string>? sourceNames = null)
  {
    var logger = NullLogger.Instance;
    var cache = new SourceCacheContext();

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
        var versions = await resource.GetAllVersionsAsync(packageId, cache, logger, cancellationToken);

        return [.. versions.Where(v => includePrerelease || !v.IsPrerelease)];
      }
      catch
      {
        return Enumerable.Empty<NuGetVersion>();
      }
    });

    var versionLists = await Task.WhenAll(versionTasks);

    return versionLists
        .SelectMany(v => v)
        .Distinct()
        .OrderByDescending(v => v);
  }


  public static async Task<Dictionary<string, IEnumerable<IPackageSearchMetadata>>> SearchAllSourcesByNameAsync(
        string searchTerm,
        CancellationToken cancellationToken,
        int take = 10,
        bool includePrerelease = false,
        List<string>? sourceNames = null)
  {
    var provider = Repository.Provider.GetCoreV3();

    var sourceProvider = new PackageSourceProvider(Settings.LoadDefaultSettings(null));
    var allSources = sourceProvider.LoadPackageSources().Where(s => s.IsEnabled);

    var selectedSources = sourceNames == null ? allSources : allSources.Where(s => sourceNames.Contains(s.Name, StringComparer.OrdinalIgnoreCase));

    var taskMap = selectedSources.ToDictionary(
        source => source.Name,
        async source =>
        {
          try
          {
            var repo = new SourceRepository(source, provider);
            var search = await repo.GetResourceAsync<PackageSearchResource>();

            return await search.SearchAsync(
                    searchTerm,
                    new SearchFilter(includePrerelease),
                    skip: 0,
                    take: take,
                    log: NullLogger.Instance,
                    cancellationToken: cancellationToken);
          }
          catch
          {
            return [];
          }
        });

    await Task.WhenAll(taskMap.Values);

    return taskMap.ToDictionary(
        kvp => kvp.Key,
        kvp => kvp.Value.Result);
  }

  public async Task<bool> PushPackageAsync(List<string> packages, string sourceUrl, string? apiKey)
  {
    var notFound = packages.FirstOrDefault(x => !File.Exists(x));
    if (notFound is not null)
    {
      throw new FileNotFoundException("Package not found", notFound);
    }

    var packageUpdateResource = await GetPackageUpdateResourceAsync(sourceUrl);

    await packageUpdateResource.Push(
        packages,
        symbolSource: null,
        timeoutInSecond: 300,
        disableBuffering: false,
        getApiKey: _ => apiKey,
        getSymbolApiKey: null,
        noServiceEndpoint: false,
        skipDuplicate: false,
        symbolPackageUpdateResource: null,
        log: NullLogger.Instance
    );

    return true;
  }

  private static async Task<PackageUpdateResource> GetPackageUpdateResourceAsync(string sourceUrl)
  {
    var packageSource = new PackageSource(sourceUrl);
    var sourceRepository = Repository.Factory.GetCoreV3(packageSource);
    return await sourceRepository.GetResourceAsync<PackageUpdateResource>();
  }
}