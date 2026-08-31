namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public sealed record InvalidationFileEntry(string Path, long MtimeTicks, long Length);

public sealed record InvalidationGlobEntry(
    string Directory,
    string Pattern,
    List<InvalidationFileEntry> Matches);

public sealed record PropertyCacheEntry(
    int SchemaVersion,
    PropertyCacheKey Key,
    Dictionary<string, string?> Properties,
    List<InvalidationFileEntry> InvalidationFiles,
    List<string> InvalidationDirectories,
    List<InvalidationGlobEntry> InvalidationGlobs,
    long CreatedAtTicks)
{
  public const int CurrentSchemaVersion = 2;
}