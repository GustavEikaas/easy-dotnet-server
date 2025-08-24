using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MessagePack;

namespace EasyDotnet.MsBuildSdk;

[MessagePackObject(true)]
public class CachedRecord
{
  public string Path { get; set; } = string.Empty;
  public string TargetPath { get; set; } = string.Empty;
  public string OutputPath { get; set; } = string.Empty;
  public List<string> Imports { get; set; } = [];
  public DateTime LastVerified { get; set; } = DateTime.UtcNow;
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // for TTL
  public string ResponseTime { get; set; } = "";
  public string CacheDir { get; set; } = "";
}

public class DotnetProjectCache
{
  private readonly ConcurrentDictionary<string, CachedRecord> _cache = new();
  private readonly string _cacheDir;
  private readonly TimeSpan _ttl = TimeSpan.FromDays(7);

  public DotnetProjectCache()
  {
    var tmpDir = Path.GetTempPath();
    _cacheDir = Path.Combine(tmpDir, "record_cache");
    Directory.CreateDirectory(_cacheDir);
  }

  /// <summary>
  /// Retrieves a record by canonical path, verifying imports.
  /// If missing/invalid, uses the provided factory to create one.
  /// </summary>
  public CachedRecord GetOrCreate(
      string path,
      Func<string,string, CachedRecord> factory)
  {
    var canonicalPath = NormalizePath(path);

    if (!_cache.TryGetValue(canonicalPath, out var record))
    {
      record = Load(canonicalPath);
      if (record != null)
      {
        record.CacheDir = _cacheDir;
        _cache[canonicalPath] = record;
      }
    }

    if (record != null)
    {
      if (IsCacheValid(record) && !IsExpired(record))
      {
        return record;
      }
    }

    // Rebuild record if not found, invalid, or expired
    var newRecord = factory(canonicalPath, _cacheDir);
    newRecord.CreatedAt = DateTime.UtcNow;
    _cache[canonicalPath] = newRecord;
    Save(canonicalPath, newRecord);
    return newRecord;
  }

  /// <summary>
  /// Normalize and canonicalize a path.
  /// </summary>
  private static string NormalizePath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

  /// <summary>
  /// Generate a safe filename for a path using SHA256.
  /// </summary>
  private string GetCacheFilePath(string canonicalPath)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath));
    var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    return Path.Combine(_cacheDir, hex + ".mpk.gz");
  }

  /// <summary>
  /// Verifies that all imports are unchanged since last check.
  /// </summary>
  private static bool IsCacheValid(CachedRecord record)
  {
    foreach (var import in record.Imports)
    {
      try
      {
        var canonicalImport = NormalizePath(import);
        var lastWrite = File.GetLastWriteTimeUtc(canonicalImport);
        if (lastWrite > record.LastVerified)
        {
          return false; // file changed since cache verification
        }
      }
      catch
      {
        return false; // missing file or error -> invalidate cache
      }
    }

    record.LastVerified = DateTime.UtcNow;
    return true;
  }

  /// <summary>
  /// Checks whether a record has exceeded its TTL.
  /// </summary>
  private bool IsExpired(CachedRecord record) => DateTime.UtcNow - record.CreatedAt > _ttl;

  /// <summary>
  /// Save one record to disk.
  /// </summary>
  private void Save(string canonicalPath, CachedRecord record)
  {
    try
    {
      var filePath = GetCacheFilePath(canonicalPath);
      Console.WriteLine(filePath);
      var data = MessagePackSerializer.Serialize(record);
      using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
      using var gzip = new GZipStream(fs, CompressionLevel.Optimal);
      gzip.Write(data, 0, data.Length);
    }
    catch
    {
      // Ignore persistence errors
    }
  }

  /// <summary>
  /// Load one record from disk if present and valid.
  /// </summary>
  private CachedRecord? Load(string canonicalPath)
  {
    try
    {
      var filePath = GetCacheFilePath(canonicalPath);
      if (!File.Exists(filePath)) return null;

      var fileAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(filePath);
      if (fileAge > _ttl)
      {
        File.Delete(filePath);
        return null; // discard stale file
      }

      using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
      using var gzip = new GZipStream(fs, CompressionMode.Decompress);
      using var ms = new MemoryStream();
      gzip.CopyTo(ms);
      ms.Position = 0;

      var record = MessagePackSerializer.Deserialize<CachedRecord>(ms.ToArray());
      if (IsExpired(record))
      {
        File.Delete(filePath);
        return null;
      }
      return record;
    }
    catch
    {
      return null; // corrupted file
    }
  }
}