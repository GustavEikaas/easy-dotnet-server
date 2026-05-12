using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.MsBuildProject.Cache;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class DiagnosticsHandler(MsBuildInstance msBuildInstance, PropertyCache propertyCache)
{
  [JsonRpcMethod("diagnostics/buildserver")]
  public BuildServerDiagnosticsResponse GetDiagnostics() =>
      new(
          RuntimeVersion: Environment.Version.ToString(),
          RuntimeVersionMajor: Environment.Version.Major,
          MsBuildVersion: msBuildInstance.Version.ToString(),
          MsBuildPath: msBuildInstance.MSBuildPath);

  [JsonRpcMethod("diagnostics/property-cache")]
  public PropertyCacheDiagnosticsResponse GetPropertyCacheStats()
  {
    var s = propertyCache.Snapshot();
    return new PropertyCacheDiagnosticsResponse(
        Evaluations: s.Evaluations,
        MemoryHits: s.MemoryHits,
        DiskHits: s.DiskHits,
        DiskRoot: propertyCache.DiskRoot);
  }
}