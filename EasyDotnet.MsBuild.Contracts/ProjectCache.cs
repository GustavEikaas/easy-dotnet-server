using System.Collections.Concurrent;
using System.Security.Cryptography;
using MessagePack;
using ZiggyCreatures.Caching.Fusion;

namespace EasyDotnet.MsBuild.Contracts;

public sealed class ProjectCache
{
  private static readonly Lazy<ProjectCache> _instance = new Lazy<ProjectCache>(() => new ProjectCache());

  public static ProjectCache Instance => _instance.Value;

  private readonly string _cacheDir;
  private readonly string _importCacheDir;

  private readonly FusionCache _memoryCache;

  private ProjectCache()
  {
    _cacheDir = Path.Combine(Path.GetTempPath(), "MyApp", "projects");
    _importCacheDir = Path.Combine(Path.GetTempPath(), "MyApp", "imports");

    Directory.CreateDirectory(_cacheDir);
    Directory.CreateDirectory(_importCacheDir);

    _memoryCache = new FusionCache(new FusionCacheOptions());
  }

  public ProjectCacheEntry GetProjectProperties(string csprojPath)
  {
    var normalizedPath = Path.GetFullPath(csprojPath);
    var cacheKey = HashPath(normalizedPath);


    var val = _memoryCache.GetOrDefault<ProjectCacheEntry>(cacheKey, null);
    if (val is not null)
    {
      if (ValidateCache(val))
        return val;
    }

    var diskEntry = LoadCacheFromDisk(cacheKey);
    if (diskEntry != null && ValidateCache(diskEntry))
    {
      _memoryCache.Set(cacheKey, diskEntry);
      return diskEntry;
    }

    var entry = ComputeProjectProperties(normalizedPath);

    SaveCacheToDisk(cacheKey, entry);
    _memoryCache.Set(cacheKey, entry);

    return entry;
  }

  private bool ValidateCache(ProjectCacheEntry entry)
  {
    // Check .csproj hash
    if (!VerifyFileHash(entry.CsprojPath, entry.CsprojHash))
      return false;

    // Check each import hash
    foreach (var import in entry.ImportHashes)
    {
      if (!VerifyImportHash(import.Key, import.Value))
        return false;
    }

    return true;
  }

  private ProjectCacheEntry LoadCacheFromDisk(string cacheKey)
  {
    var path = Path.Combine(_cacheDir, $"{cacheKey}_v1.msgpack");
    if (!File.Exists(path)) return null;

    try
    {
      var bytes = File.ReadAllBytes(path);
      return MessagePackSerializer.Deserialize<ProjectCacheEntry>(bytes);
    }
    catch
    {
      return null; // corrupted cache
    }
  }

  private bool VerifyImportHash(string importPath, string expectedHash)
  {
    if (string.IsNullOrEmpty(importPath) || string.IsNullOrEmpty(expectedHash))
      return false;

    if (!File.Exists(importPath))
      return false;

    try
    {
      var importKey = HashPath(importPath);
      var hashFile = Path.Combine(_importCacheDir, $"{importKey}.hash");

      // Case 1: hash file exists
      if (File.Exists(hashFile))
      {
        var cachedHash = File.ReadAllText(hashFile);

        // If expectedHash matches cachedHash → good
        if (string.Equals(expectedHash, cachedHash, StringComparison.OrdinalIgnoreCase))
        {
          // Check if import file is newer than hash file
          var importMtime = File.GetLastWriteTimeUtc(importPath);
          var hashMtime = File.GetLastWriteTimeUtc(hashFile);

          if (importMtime <= hashMtime)
            return true; // still valid
        }
      }

      // Case 2: either hash file is missing or stale → recompute
      var actualHash = ComputeFileHash(importPath);

      // Update shared hash file
      File.WriteAllText(hashFile, actualHash);

      // Return true only if matches expected
      return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
      return false; // e.g., IO errors
    }
  }


  private static bool VerifyFileHash(string filePath, string expectedHash)
  {
    if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(expectedHash))
      return false;

    if (!File.Exists(filePath))
      return false;

    try
    {
      var actualHash = ComputeFileHash(filePath);
      return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
      // e.g., file locked, IO error
      return false;
    }
  }

  private void SaveCacheToDisk(string cacheKey, ProjectCacheEntry entry)
  {
    var tempPath = Path.Combine(_cacheDir, $"{cacheKey}.tmp");
    var finalPath = Path.Combine(_cacheDir, $"{cacheKey}_v1.msgpack");

    var bytes = MessagePackSerializer.Serialize(entry);
    File.WriteAllBytes(tempPath, bytes);
    File.Move(tempPath, finalPath, overwrite: true);
  }

  private ProjectCacheEntry ComputeProjectProperties(string csprojPath)
  {
    // 1. Compute hash of .csproj
    var csprojHash = ComputeFileHash(csprojPath);

    // 2. Determine imported targets/props
    var importPaths = ParseImports(csprojPath);

    var importHashes = new ConcurrentDictionary<string, string>();
    foreach (var importPath in importPaths)
    {
      var importHash = LoadOrComputeImportHash(importPath);
      importHashes[importPath] = importHash;
    }

    // 3. Gather project properties
    var props = QueryProjectProperties(csprojPath);

    return new ProjectCacheEntry
    {
      CsprojPath = csprojPath,
      CsprojHash = csprojHash,
      ImportHashes = importHashes,
      Properties = props
    };
  }

  private string LoadOrComputeImportHash(string importPath)
  {
    var hashFile = Path.Combine(_importCacheDir, $"{HashPath(importPath)}.hash");
    if (File.Exists(hashFile))
    {
      var cachedHash = File.ReadAllText(hashFile);
      var mtime = File.GetLastWriteTimeUtc(importPath);
      if (mtime <= File.GetLastWriteTimeUtc(hashFile))
        return cachedHash;
    }

    var hash = ComputeFileHash(importPath);
    File.WriteAllText(hashFile, hash);
    return hash;
  }

  private static string HashPath(string path)
  {
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path));
    return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
  }

  private static string ComputeFileHash(string filePath)
  {
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var hash = sha.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
  }

  // Placeholder for real project query
  private ProjectProperties QueryProjectProperties(string csprojPath) => new()
  {
    Name = Path.GetFileNameWithoutExtension(csprojPath),
    TargetPath = "...",
    OutputPath = "...",
    TFM = "net7.0"
  };

  // Placeholder to parse imports
  private string[] ParseImports(string csprojPath) => Array.Empty<string>();
}

// Cache entry classes
[MessagePackObject(true)]
public class ProjectCacheEntry
{
  [Key(0)] public string CsprojPath { get; set; }
  [Key(1)] public string CsprojHash { get; set; }
  [Key(2)] public ConcurrentDictionary<string, string> ImportHashes { get; set; }
  [Key(3)] public ProjectProperties Properties { get; set; }
}

[MessagePackObject(true)]
public class ProjectProperties
{
  [Key(0)] public string Name { get; set; }
  [Key(1)] public string TargetPath { get; set; }
  [Key(2)] public string OutputPath { get; set; }
  [Key(3)] public string TFM { get; set; }
}