using System;
using System.IO;

namespace EasyDotnet.IDE.Utils;

/// <summary>
/// Manages application paths and ensures directories exist
/// </summary>
public static class AppPaths
{
  private static readonly Lazy<string> _cacheDirectory = new(InitializeCacheDirectory);

  /// <summary>
  /// Gets the application's cache directory path (e.g., /tmp/easy-dotnet or %TEMP%\easy-dotnet)
  /// Directory is guaranteed to exist.
  /// </summary>
  public static string CacheDirectory => _cacheDirectory.Value;

  /// <summary>
  /// Gets the path for the update check cache file
  /// </summary>
  public static string UpdateCheckCacheFile => Path.Combine(CacheDirectory, "last-update-check.txt");

  /// <summary>
  /// Gets the path for a specific cache file
  /// </summary>
  public static string GetCacheFilePath(string filename) => Path.Combine(CacheDirectory, filename);

  private static string InitializeCacheDirectory()
  {
    // Use platform-appropriate temp directory
    var tempPath = Path.GetTempPath();
    var appCachePath = Path.Combine(tempPath, "easy-dotnet");

    // Ensure directory exists
    if (!Directory.Exists(appCachePath))
    {
      Directory.CreateDirectory(appCachePath);
    }

    return appCachePath;
  }

  /// <summary>
  /// Clears all cache files (useful for debugging or cleanup)
  /// </summary>
  public static void ClearCache()
  {
    if (Directory.Exists(CacheDirectory))
    {
      foreach (var file in Directory.GetFiles(CacheDirectory))
      {
        try
        {
          File.Delete(file);
        }
        catch
        {
          // Ignore errors during cleanup
        }
      }
    }
  }
}
