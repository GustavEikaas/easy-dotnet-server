using System.Collections.Concurrent;
using EasyDotnet.BuildServer.Contracts;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.BuildHost;

/// <summary>
/// Reasons a cache entry was invalidated — passed to subscribers so they
/// can react differently (e.g. restore-driven vs user-driven invalidation).
/// </summary>
public enum CacheInvalidationReason
{
  Restore,
  ExplicitInvalidate,
  EvaluationFailed,
  ClearedAll,
}

public sealed class ProjectEvaluationCache(ILogger<ProjectEvaluationCache> logger)
{
  private readonly ConcurrentDictionary<(string Path, string Config, string Platform, bool ComputeRunArguments), TaskCompletionSource<List<ProjectEvaluationResult>>> _store = new();

  /// <summary>
  /// Fired when one or more entries are removed from the cache.
  /// Subscribers receive the affected paths and the reason.
  /// </summary>
  public event Action<IReadOnlyList<string>, CacheInvalidationReason>? Invalidated;

  /// <summary>
  /// Atomically gets or registers a TCS for the given key.
  /// Returns (tcs, isNew) — isNew=true means the caller owns the fetch.
  /// </summary>
  public (TaskCompletionSource<List<ProjectEvaluationResult>> Tcs, bool IsNew) GetOrRegister(
      string path, string config, string? platform, bool computeRunArguments = false)
  {
    var key = (path, config, platform ?? "", computeRunArguments);
    var tcs = new TaskCompletionSource<List<ProjectEvaluationResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var actual = _store.GetOrAdd(key, tcs);
    var isNew = ReferenceEquals(actual, tcs);

    if (isNew)
    {
      logger.LogDebug("Cache miss — queuing fetch for {Path} [{Config}|{Platform}]", path, config, platform ?? "");
    }
    else
    {
      logger.LogDebug("Cache hit — awaiting existing fetch for {Path} [{Config}|{Platform}]", path, config, platform ?? "");
    }

    return (actual, isNew);
  }

  /// <summary>
  /// Marks a fetch as complete. Removes the entry if evaluation failed
  /// so the next caller retries rather than getting a cached failure.
  /// </summary>
  public void Complete(string path, string config, string? platform, bool computeRunArguments, List<ProjectEvaluationResult> results)
  {
    var key = (path, config, platform ?? "", computeRunArguments);
    var failed = results.Count == 0 || results.Exists(r => !r.Success);

    if (failed)
    {
      _store.TryRemove(key, out var tcs);
      tcs?.TrySetResult(results);
      logger.LogDebug("Cache evicted (evaluation failed) for {Path} [{Config}|{Platform}]", path, config, platform ?? "");
      Invalidated?.Invoke([path], CacheInvalidationReason.EvaluationFailed);
    }
    else if (_store.TryGetValue(key, out var tcs))
    {
      tcs.TrySetResult(results);
      logger.LogDebug("Cache populated for {Path} [{Config}|{Platform}]", path, config, platform ?? "");
    }
  }

  /// <summary>
  /// Faults a pending fetch and removes it from the cache so the next
  /// caller retries cleanly.
  /// </summary>
  public void Fault(string path, string config, string? platform, bool computeRunArguments, Exception ex)
  {
    var key = (path, config, platform ?? "", computeRunArguments);
    _store.TryRemove(key, out var tcs);
    tcs?.TrySetException(ex);
    logger.LogDebug("Cache faulted for {Path} [{Config}|{Platform}]: {Message}", path, config, platform ?? "", ex.Message);
    Invalidated?.Invoke([path], CacheInvalidationReason.EvaluationFailed);
  }

  /// <summary>
  /// Removes a single entry. Used after an explicit invalidate/rebuild.
  /// </summary>
  public void Invalidate(string path, string? config = null, string? platform = null)
  {
    if (config is null)
    {
      foreach (var key in _store.Keys.Where(key => string.Equals(key.Path, path, StringComparison.OrdinalIgnoreCase)).ToList())
      {
        _store.TryRemove(key, out _);
      }
      logger.LogInformation("Cache invalidated for {Path} [all configurations]", path);
    }
    else
    {
      foreach (var key in _store.Keys.Where(key =>
          string.Equals(key.Path, path, StringComparison.OrdinalIgnoreCase)
          && string.Equals(key.Config, config, StringComparison.OrdinalIgnoreCase)
          && string.Equals(key.Platform, platform ?? "", StringComparison.OrdinalIgnoreCase)).ToList())
      {
        _store.TryRemove(key, out _);
      }
      logger.LogInformation("Cache invalidated for {Path} [{Config}|{Platform}]", path, config, platform ?? "");
    }
    Invalidated?.Invoke([path], CacheInvalidationReason.ExplicitInvalidate);
  }

  /// <summary>
  /// Removes all entries. Used after a restore so the next eval picks up
  /// freshly written project.assets.json files.
  /// </summary>
  public void Clear(CacheInvalidationReason reason = CacheInvalidationReason.ClearedAll)
  {
    var paths = _store.Keys.Select(k => Path.GetFileNameWithoutExtension(k.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    _store.Clear();
    logger.LogInformation("Cache cleared ({Reason}) — {Count} entries evicted: {Paths}", reason, paths.Count, string.Join(", ", paths));
    if (paths.Count > 0)
    {
      Invalidated?.Invoke(paths, reason);
    }
  }
}