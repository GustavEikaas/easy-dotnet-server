using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public interface IAppPathsService
{
  string CacheDirectory { get; }
  string UpdateCheckCacheFile { get; }
  void ClearCache();
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