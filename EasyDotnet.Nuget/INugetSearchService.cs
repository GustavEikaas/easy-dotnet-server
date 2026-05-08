using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace EasyDotnet.Nuget;

public interface INugetSearchService
{
  List<PackageSource> GetSources();

  Task<IReadOnlyList<NuGetVersion>> GetVersionsAsync(
      string packageId,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null);

  Task<IReadOnlyList<NugetPackageHit>> SearchByPrefixAsync(
      string searchTerm,
      int take,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null);

  Task<IReadOnlyDictionary<string, IReadOnlyList<IPackageSearchMetadata>>> SearchAllSourcesAsync(
      string searchTerm,
      int take,
      bool includePrerelease,
      CancellationToken cancellationToken,
      IReadOnlyList<string>? sourceNames = null);
}