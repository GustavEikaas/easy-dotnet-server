namespace EasyDotnet.BuildServer.MsBuildProject;

/// <summary>
/// Cache model for successful single-file builds, written as JSON to build-success.cache.
/// Used to determine if a rebuild is needed.
/// </summary>
public record SingleFileCacheEntry(
    Dictionary<string, string> GlobalProperties,
    Dictionary<string, DateTime> ImplicitBuildFiles
);
