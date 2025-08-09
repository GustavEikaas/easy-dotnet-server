using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.MsBuild.Contracts;
using ZiggyCreatures.Caching.Fusion;

namespace EasyDotnet.Services;

public class MsBuildService(IMsBuildHostManager manager, IFusionCache fusionCache)
{
  public async Task<BuildResult> RequestBuildAsync(string targetPath, string configuration, CancellationToken cancellationToken = default)
  {
    //TODO: resolve sdk/framework relation and start appropriate server
    var sdkBuildHost = await manager.GetOrStartClientAsync(BuildClientType.Sdk);
    var result = await sdkBuildHost.BuildAsync(targetPath, configuration);
    return result;
  }

  public async Task<DotnetProjectProperties> QueryProjectProperties(string targetPath, string configuration, string? targetFramework, CancellationToken cancellationToken = default)
  {
    var cacheKey = BuildCacheKey(targetPath, configuration, targetFramework);

    var cachedResult = await fusionCache.GetOrSetAsync(cacheKey, async () =>
    {
      var sdkBuildHost = await manager.GetOrStartClientAsync(BuildClientType.Sdk);
      var result = await sdkBuildHost.QueryProjectProperties(targetPath, configuration, targetFramework ?? "Default");
      return new CachedDotnetProjectProperties(CacheTime: DateTime.Now, Properties: result);
    }, new FusionCacheEntryOptions(duration: TimeSpan.MaxValue), token: cancellationToken);

    var res = await cachedResult();
    var staleCache = res.Properties.Sources.Any(x => DateTime.Compare(x.MTime, res.CacheTime) > 0);

    if (staleCache)
    {
      fusionCache.Remove(cacheKey, null, cancellationToken);
      return await QueryProjectProperties(targetPath, configuration, targetFramework, cancellationToken);
    }

    return res.Properties;
  }

  private static string BuildCacheKey(string targetPath, string configuration, string? targetFramework) => string.Join("|", targetPath, configuration, targetFramework);
}


internal sealed record CachedDotnetProjectProperties(DateTime CacheTime, DotnetProjectProperties Properties);