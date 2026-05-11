using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.MsBuild.Project;
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
    IClientService clientService,
    ILogger<NugetService> logger,
    IProcessQueue processLimiter,
    INugetSearchService searchService,
    INugetSettingsProvider settingsProvider)
{

  private static (string Command, string Arguments) GetCommandAndArguments(
      MSBuildProjectType type,
      string targetPath) => type switch
      {
        MSBuildProjectType.SDK => ("dotnet", $"restore \"{targetPath}\" "),
        MSBuildProjectType.VisualStudio => ("nuget", $"restore \"{targetPath}\""),
        _ => throw new InvalidOperationException("Unknown MSBuild type")
      };

  public async Task<RestoreResult> RestorePackagesAsync(string targetPath, CancellationToken cancellationToken)
  {
    var (command, args) = GetCommandAndArguments(clientService.UseVisualStudio ? MSBuildProjectType.VisualStudio : MSBuildProjectType.SDK, targetPath);
    logger.LogInformation("Starting restore `{command} {args}`", command, args);
    var (success, stdout, stderr) = await processLimiter.RunProcessAsync(command, args, new ProcessOptions(KillOnTimeout: true), cancellationToken);

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

  public ISettings GetSettings() => settingsProvider.GetSettings();

  public List<PackageSource> GetSources() => searchService.GetSources();

  public Task<IReadOnlyList<NuGetVersion>> GetPackageVersionsAsync(
      string packageId,
      CancellationToken cancellationToken,
      bool includePrerelease = false,
      List<string>? sourceNames = null)
      => searchService.GetVersionsAsync(packageId, includePrerelease, cancellationToken, sourceNames);

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
}