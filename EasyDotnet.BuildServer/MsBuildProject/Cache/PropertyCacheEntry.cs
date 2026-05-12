namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public sealed record InvalidationFileEntry(string Path, long MtimeTicks, long Length);

public sealed record PropertyCacheEntry(
    int SchemaVersion,
    PropertyCacheKey Key,
    Dictionary<string, string?> Properties,
    List<InvalidationFileEntry> InvalidationFiles,
    List<string> InvalidationDirectories,
    long CreatedAtTicks)
{
  public const int CurrentSchemaVersion = 1;
}
