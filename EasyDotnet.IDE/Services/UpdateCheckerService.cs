using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public class UpdateCheckerService(
  NugetService nugetService,
  INotificationService notificationService,
  ILogger<UpdateCheckerService> logger,
  IAppPathsService appPaths)
{
  private readonly TimeSpan _updateCheckInterval = TimeSpan.FromHours(6);
  private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
  private const string PackageId = "EasyDotnet";

  private string? _cachedUpdateMessage;

  public async Task CheckForUpdates(CancellationToken cancellationToken)
  {
    try
    {
      var lastCheck = await GetLastCheckTimeAsync();
      if (lastCheck.HasValue && DateTime.UtcNow - lastCheck.Value < _updateCheckInterval)
      {
        logger.LogDebug("Skipping update check, last checked {TimeAgo} ago", DateTime.UtcNow - lastCheck.Value);

        if (!string.IsNullOrEmpty(_cachedUpdateMessage))
        {
          logger.LogInformation("Cached update available: {Message}", _cachedUpdateMessage);
        }
        return;
      }

      var currentVersion = Assembly.GetExecutingAssembly().GetName().Version!;

      logger.LogDebug("Checking for updates to {PackageId} (current: {Version})", PackageId, currentVersion);

      var versions = await nugetService.GetPackageVersionsAsync(PackageId, cancellationToken, false);

      var newerVersions = versions
        .Where(x => x.Version > currentVersion)
        .OrderByDescending(x => x.Version)
        .ToList();

      if (newerVersions.Count == 0)
      {
        logger.LogDebug("No updates available");
        await SaveLastCheckTimeAsync(DateTime.UtcNow);
        _cachedUpdateMessage = null;
        return;
      }

      var highest = newerVersions[0];
      var updateType = GetUpdateType(currentVersion, highest.Version);

      _cachedUpdateMessage = $"{currentVersion} -> {highest.Version} ({updateType})";

      logger.LogInformation("Update available: {Message}", _cachedUpdateMessage);

      await SaveLastCheckTimeAsync(DateTime.UtcNow);
      await notificationService.NotifyUpdateAvailable(currentVersion, highest.Version, updateType);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to check for updates");
    }
  }

  private async Task<DateTime?> GetLastCheckTimeAsync()
  {
    try
    {
      var cacheFile = appPaths.UpdateCheckCacheFile;

      if (!File.Exists(cacheFile))
        return null;

      var content = await File.ReadAllTextAsync(cacheFile);
      var cacheData = JsonSerializer.Deserialize<UpdateCheckCache>(content);

      return cacheData?.LastCheckTime;
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to read update check cache");
      return null;
    }
  }

  private async Task SaveLastCheckTimeAsync(DateTime checkTime)
  {
    try
    {
      var cacheFile = appPaths.UpdateCheckCacheFile;
      var cacheData = new UpdateCheckCache(checkTime);
      var json = JsonSerializer.Serialize(cacheData, JsonSerializerOptions);

      await File.WriteAllTextAsync(cacheFile, json);
      logger.LogDebug("Saved update check time to cache");
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to save update check cache");
    }
  }

  private static string GetUpdateType(Version current, Version latest)
  {
    if (latest.Major > current.Major)
      return "major";
    if (latest.Minor > current.Minor)
      return "minor";
    if (latest.Build > current.Build)
      return "patch";
    return "revision";
  }

  private record UpdateCheckCache(DateTime LastCheckTime);
}