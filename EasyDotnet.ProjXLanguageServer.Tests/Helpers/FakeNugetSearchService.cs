using EasyDotnet.Nuget;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace EasyDotnet.ProjXLanguageServer.Tests.Helpers;

public sealed class FakeNugetSearchService : INugetSearchService
{
  public Func<string, IReadOnlyList<NugetPackageHit>>? OnSearch { get; set; }
  public Func<string, IReadOnlyList<NuGetVersion>>? OnVersions { get; set; }
  public bool ShouldThrow { get; set; }
  public int SearchCalls { get; private set; }
  public int VersionCalls { get; private set; }

  public List<PackageSource> GetSources() => [];

  public Task<IReadOnlyList<NuGetVersion>> GetVersionsAsync(
      string packageId,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null)
  {
    VersionCalls++;
    if (ShouldThrow) throw new InvalidOperationException("simulated");
    return Task.FromResult(OnVersions?.Invoke(packageId) ?? (IReadOnlyList<NuGetVersion>)[]);
  }

  public Task<IReadOnlyList<NugetPackageHit>> SearchByPrefixAsync(
      string searchTerm,
      int take,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null)
  {
    SearchCalls++;
    if (ShouldThrow) throw new InvalidOperationException("simulated");
    return Task.FromResult(OnSearch?.Invoke(searchTerm) ?? (IReadOnlyList<NugetPackageHit>)[]);
  }

  public Task<IReadOnlyDictionary<string, IReadOnlyList<IPackageSearchMetadata>>> SearchAllSourcesAsync(
      string searchTerm,
      int take,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null)
      => Task.FromResult((IReadOnlyDictionary<string, IReadOnlyList<IPackageSearchMetadata>>)new Dictionary<string, IReadOnlyList<IPackageSearchMetadata>>());
}