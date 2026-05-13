using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using EasyDotnet.BuildServer.Logging;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public sealed record PropertyCacheStats(long Evaluations, long MemoryHits, long DiskHits);

public sealed class PropertyCache
{
  private readonly InputPredictor _predictor;
  private readonly Logger _logger;
  private readonly ConcurrentDictionary<PropertyCacheKey, PropertyCacheEntry> _memory = new();
  private readonly ConcurrentDictionary<PropertyCacheKey, SemaphoreSlim> _locks = new();

  private long _evaluations;
  private long _memoryHits;
  private long _diskHits;

  private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

  public PropertyCache(InputPredictor predictor, Logger logger)
  {
    _predictor = predictor;
    _logger = logger;
    DiskRoot = ResolveCacheRoot();
    try
    {
      Directory.CreateDirectory(DiskRoot);
    }
    catch (Exception ex)
    {
      _logger.LogWarning("PropertyCache: could not create disk root {Root}: {Message}", DiskRoot, ex.Message);
    }
  }

  public string DiskRoot { get; }

  public PropertyCacheStats Snapshot() => new(
      Interlocked.Read(ref _evaluations),
      Interlocked.Read(ref _memoryHits),
      Interlocked.Read(ref _diskHits));

  public PropertyCacheEntry GetOrEvaluate(
      PropertyCacheKey key,
      ProjectCollection projectCollection,
      Func<(Project Project, IReadOnlyDictionary<string, string?> Properties)> evaluate,
      CancellationToken cancellationToken)
  {
    if (_memory.TryGetValue(key, out var entry) && IsValid(entry))
    {
      Interlocked.Increment(ref _memoryHits);
      return entry;
    }

    var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    sem.Wait(cancellationToken);
    try
    {
      if (_memory.TryGetValue(key, out entry) && IsValid(entry))
      {
        Interlocked.Increment(ref _memoryHits);
        return entry;
      }

      if (TryLoadFromDisk(key, out var diskEntry) && diskEntry!.SchemaVersion == PropertyCacheEntry.CurrentSchemaVersion && IsValid(diskEntry))
      {
        _memory[key] = diskEntry;
        Interlocked.Increment(ref _diskHits);
        return diskEntry;
      }

      var (project, properties) = evaluate();
      try
      {
        var instance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
        var prediction = _predictor.Predict(instance);

        var manifest = new List<InvalidationFileEntry>(prediction.InputFiles.Count);
        foreach (var f in prediction.InputFiles)
        {
          var fi = new FileInfo(f);
          if (fi.Exists)
          {
            manifest.Add(new InvalidationFileEntry(f, fi.LastWriteTimeUtc.Ticks, fi.Length));
          }
        }

        // For directories, also enumerate current files and add their entries so directory drift detection has a baseline.
        var dirSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in prediction.InputDirectories)
        {
          if (!Directory.Exists(d)) continue;
          dirSet.Add(d);
          foreach (var f in Directory.EnumerateFiles(d))
          {
            if (manifest.Any(m => string.Equals(m.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
            var fi = new FileInfo(f);
            if (fi.Exists)
            {
              manifest.Add(new InvalidationFileEntry(f, fi.LastWriteTimeUtc.Ticks, fi.Length));
            }
          }
        }

        var propertiesCopy = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in properties)
        {
          propertiesCopy[kv.Key] = kv.Value;
        }

        var newEntry = new PropertyCacheEntry(
            PropertyCacheEntry.CurrentSchemaVersion,
            key,
            propertiesCopy,
            manifest,
            [.. dirSet],
            DateTime.UtcNow.Ticks);

        _memory[key] = newEntry;
        TryWriteToDisk(key, newEntry);
        Interlocked.Increment(ref _evaluations);
        return newEntry;
      }
      finally
      {
        try { projectCollection.UnloadProject(project); } catch { }
      }
    }
    finally
    {
      sem.Release();
    }
  }

  private bool IsValid(PropertyCacheEntry entry)
  {
    foreach (var f in entry.InvalidationFiles)
    {
      var fi = new FileInfo(f.Path);
      if (!fi.Exists) return false;
      if (fi.LastWriteTimeUtc.Ticks != f.MtimeTicks) return false;
      if (fi.Length != f.Length) return false;
    }

    foreach (var dir in entry.InvalidationDirectories)
    {
      if (!Directory.Exists(dir)) return false;

      var current = new HashSet<string>(Directory.EnumerateFiles(dir), StringComparer.OrdinalIgnoreCase);
      var cached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var m in entry.InvalidationFiles)
      {
        var parent = Path.GetDirectoryName(m.Path);
        if (parent != null && string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
        {
          cached.Add(m.Path);
        }
      }

      if (!current.SetEquals(cached)) return false;
    }

    return true;
  }

  private string DiskPath(PropertyCacheKey key) => Path.Combine(DiskRoot, key.ToDiskFileName());

  private bool TryLoadFromDisk(PropertyCacheKey key, out PropertyCacheEntry? entry)
  {
    entry = null;
    var path = DiskPath(key);
    if (!File.Exists(path)) return false;
    try
    {
      var json = File.ReadAllText(path);
      entry = JsonSerializer.Deserialize<PropertyCacheEntry>(json, JsonOpts);
      return entry != null;
    }
    catch (Exception ex)
    {
      _logger.LogWarning("PropertyCache: failed to read {Path}: {Message}", path, ex.Message);
      return false;
    }
  }

  private void TryWriteToDisk(PropertyCacheKey key, PropertyCacheEntry entry)
  {
    var path = DiskPath(key);
    var tmp = path + ".tmp";
    try
    {
      var json = JsonSerializer.Serialize(entry, JsonOpts);
      File.WriteAllText(tmp, json);
      if (File.Exists(path)) File.Delete(path);
      File.Move(tmp, path);
    }
    catch (Exception ex)
    {
      _logger.LogWarning("PropertyCache: failed to write {Path}: {Message}", path, ex.Message);
      try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
    }
  }

  private static string ResolveCacheRoot()
  {
    var overrideDir = Environment.GetEnvironmentVariable("EASYDOTNET_PROPERTY_CACHE_DIR");
    if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "EasyDotnet", "property-cache");
    }

    var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    var root = !string.IsNullOrEmpty(xdg)
        ? xdg
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
    return Path.Combine(root, "EasyDotnet", "property-cache");
  }
}