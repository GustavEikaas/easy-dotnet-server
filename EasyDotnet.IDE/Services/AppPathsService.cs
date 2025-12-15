using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public interface IAppPathsService
{
  string CacheDirectory { get; }
  string UpdateCheckCacheFile { get; }
  string GetCacheFilePath(string filename);
  void ClearCache();
  bool EnsureDirectoryExists(string path);
}

public class AppPathsService : IAppPathsService
{
  private readonly ILogger<AppPathsService> _logger;
  private readonly Lazy<string> _cacheDirectory;

  public AppPathsService(ILogger<AppPathsService> logger)
  {
    _logger = logger;
    _cacheDirectory = new Lazy<string>(InitializeCacheDirectory);
  }

  /// <summary>
  /// Gets the application's cache directory path (e.g., /tmp/easy-dotnet or %TEMP%\easy-dotnet)
  /// Directory is guaranteed to exist.
  /// </summary>
  public string CacheDirectory => _cacheDirectory.Value;

  /// <summary>
  /// Gets the path for the update check cache file
  /// </summary>
  public string UpdateCheckCacheFile => Path.Combine(CacheDirectory, "last-update-check.txt");

  /// <summary>
  /// Gets the path for a specific cache file
  /// </summary>
  public string GetCacheFilePath(string filename)
  {
    if (string.IsNullOrWhiteSpace(filename))
    {
      _logger.LogWarning("Attempted to get cache file path with empty filename");
      throw new ArgumentException("Filename cannot be null or empty", nameof(filename));
    }

    return Path.Combine(CacheDirectory, filename);
  }

  private string InitializeCacheDirectory()
  {
    var tempPath = Path.GetTempPath();
    var appCachePath = Path.Combine(tempPath, "easy-dotnet");

    if (!Directory.Exists(appCachePath))
    {
      Directory.CreateDirectory(appCachePath);
      _logger.LogInformation("Created cache directory at {Path}", appCachePath);
    }
    else
    {
      _logger.LogDebug("Using existing cache directory at {Path}", appCachePath);
    }

    return appCachePath;
  }

  /// <summary>
  /// Ensures a directory exists, creating it if necessary
  /// </summary>
  public bool EnsureDirectoryExists(string path)
  {
    try
    {
      if (!Directory.Exists(path))
      {
        Directory.CreateDirectory(path);
        _logger.LogDebug("Created directory at {Path}", path);
      }
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create directory at {Path}", path);
      return false;
    }
  }

  /// <summary>
  /// Clears all cache files (useful for debugging or cleanup)
  /// </summary>
  public void ClearCache()
  {
    try
    {
      if (!Directory.Exists(CacheDirectory))
      {
        _logger.LogDebug("Cache directory does not exist, nothing to clear");
        return;
      }
      Directory.Delete(CacheDirectory, recursive: true);
      _logger.LogInformation("Cache cleared successfully");
      InitializeCacheDirectory();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to clear cache directory");
    }
  }
}