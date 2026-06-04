using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.Nuget;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using StreamJsonRpc;

namespace EasyDotnet.Services;

public sealed record RestoreResult(bool Success, IAsyncEnumerable<string> Errors, IAsyncEnumerable<string> Warnings);

public class NugetService(
    ILogger<NugetService> logger,
    INugetSearchService searchService,
    INugetSettingsProvider settingsProvider,
    IBuildHostManager buildHostManager)
{

  public async Task<RestoreResult> RestorePackagesAsync(string targetPath, CancellationToken cancellationToken)
  {
    logger.LogInformation("Starting restore for {TargetPath}", targetPath);

    var errors = new List<string>();
    var warnings = new List<string>();
    var success = true;

    await foreach (var result in buildHostManager.RestoreNugetPackagesAsync(
        new RestoreRequest([Path.GetFullPath(targetPath)]),
        cancellationToken))
    {
      success &= result.Success;

      if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
      {
        errors.Add($"{result.ProjectPath}: {result.ErrorMessage}");
      }

      foreach (var diagnostic in result.Output?.Diagnostics ?? [])
      {
        var message = FormatDiagnostic(diagnostic);
        if (diagnostic.Severity == BuildDiagnosticSeverity.Error)
        {
          errors.Add(message);
        }
        else if (diagnostic.Severity == BuildDiagnosticSeverity.Warning)
        {
          warnings.Add(message);
        }
      }
    }

    return new RestoreResult(success && errors.Count == 0, errors.AsAsyncEnumerable(), warnings.AsAsyncEnumerable());
  }

  public ISettings GetSettings() => settingsProvider.GetSettings();

  public List<PackageSource> GetSources() => searchService.GetSources();

  public Task<IReadOnlyList<NuGetVersion>> GetPackageVersionsAsync(
      string packageId,
      CancellationToken cancellationToken,
      bool includePrerelease = false,
      List<string>? sourceNames = null)
      => searchService.GetVersionsAsync(packageId, includePrerelease, cancellationToken, sourceNames);

  public async Task<IReadOnlyList<NuGetVersion>> GetNugetOrgPackageVersionsAsync(
      string packageId,
      CancellationToken cancellationToken,
      bool includePrerelease = false)
  {
    DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);

    using var cache = new SourceCacheContext();
    var source = new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org");
    var repo = new SourceRepository(source, Repository.Provider.GetCoreV3());
    var resource = await repo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
    var versions = await resource.GetAllVersionsAsync(packageId, cache, NullLogger.Instance, cancellationToken);

    return [.. versions
        .Where(v => includePrerelease || !v.IsPrerelease)
        .Distinct()
        .OrderByDescending(v => v)];
  }

  public Task<IReadOnlyDictionary<string, IReadOnlyList<IPackageSearchMetadata>>> SearchAllSourcesByNameAsync(
      string searchTerm,
      CancellationToken cancellationToken,
      int take = 10,
      bool includePrerelease = false,
      List<string>? sourceNames = null)
      => searchService.SearchAllSourcesAsync(searchTerm, take, includePrerelease, cancellationToken, sourceNames);

  public async Task<bool> PushPackageAsync(List<string> packages, string sourceUrl, string? apiKey)
  {
    DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);
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

  private static string FormatDiagnostic(BuildDiagnostic diagnostic)
  {
    var location = string.IsNullOrWhiteSpace(diagnostic.File)
        ? diagnostic.ProjectFile
        : diagnostic.File;

    var prefix = string.IsNullOrWhiteSpace(location)
        ? string.Empty
        : $"{location}({diagnostic.LineNumber},{diagnostic.ColumnNumber}): ";

    var code = string.IsNullOrWhiteSpace(diagnostic.Code)
        ? string.Empty
        : $"{diagnostic.Code}: ";

    return $"{prefix}{code}{diagnostic.Message}";
  }
}