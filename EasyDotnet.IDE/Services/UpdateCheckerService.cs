using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.IDE.Interfaces;
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
  private string? _cachedRoslynUpdateMessage;

  public async Task CheckForUpdates(CancellationToken cancellationToken)
  {
    await CheckEasyDotnetUpdates(cancellationToken);
    await CheckRoslynToolUpdates(cancellationToken);
  }

  private async Task CheckEasyDotnetUpdates(CancellationToken cancellationToken)
  {
    try
    {
      var lastCheck = await GetLastCheckTimeAsync(appPaths.UpdateCheckCacheFile);
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

      var versions = await nugetService.GetNugetOrgPackageVersionsAsync(PackageId, cancellationToken, false);

      var newerVersions = versions
        .Where(x => x.Version > currentVersion)
        .OrderByDescending(x => x.Version)
        .ToList();

      if (newerVersions.Count == 0)
      {
        logger.LogDebug("No updates available");
        await SaveLastCheckTimeAsync(appPaths.UpdateCheckCacheFile, DateTime.UtcNow);
        _cachedUpdateMessage = null;
        return;
      }

      var highest = newerVersions[0];
      var updateType = GetUpdateType(currentVersion, highest.Version);

      _cachedUpdateMessage = $"{currentVersion} -> {highest.Version} ({updateType})";

      logger.LogInformation("Update available: {Message}", _cachedUpdateMessage);

      await SaveLastCheckTimeAsync(appPaths.UpdateCheckCacheFile, DateTime.UtcNow);
      await notificationService.NotifyUpdateAvailable(currentVersion, highest.Version, updateType);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to check for updates");
    }
  }

  private async Task CheckRoslynToolUpdates(CancellationToken cancellationToken)
  {
    try
    {
      var lastCheck = await GetLastCheckTimeAsync(appPaths.RoslynUpdateCheckCacheFile);
      if (lastCheck.HasValue && DateTime.UtcNow - lastCheck.Value < _updateCheckInterval)
      {
        logger.LogDebug("Skipping Roslyn tool update check, last checked {TimeAgo} ago", DateTime.UtcNow - lastCheck.Value);

        if (!string.IsNullOrEmpty(_cachedRoslynUpdateMessage))
        {
          logger.LogInformation("Cached Roslyn update available: {Message}", _cachedRoslynUpdateMessage);
        }
        return;
      }

      var status = await RoslynToolService.GetStatusAsync(cancellationToken);
      if (!status.IsInstalled)
      {
        logger.LogDebug("Skipping Roslyn tool update check because {PackageId} is not installed", RoslynToolService.PackageId);
        await SaveLastCheckTimeAsync(appPaths.RoslynUpdateCheckCacheFile, DateTime.UtcNow);
        _cachedRoslynUpdateMessage = null;
        return;
      }

      var currentVersion = RoslynToolService.TryParseVersion(status.Version);
      if (currentVersion is null)
      {
        logger.LogDebug("Skipping Roslyn tool update check because installed version could not be parsed: {Version}", status.Version);
        await SaveLastCheckTimeAsync(appPaths.RoslynUpdateCheckCacheFile, DateTime.UtcNow);
        _cachedRoslynUpdateMessage = null;
        return;
      }

      logger.LogDebug("Checking for updates to {PackageId} (current: {Version})", RoslynToolService.PackageId, currentVersion);

      var versions = await nugetService.GetNugetOrgPackageVersionsAsync(RoslynToolService.PackageId, cancellationToken, true);
      var latest = versions.OrderByDescending(x => x).FirstOrDefault();

      await SaveLastCheckTimeAsync(appPaths.RoslynUpdateCheckCacheFile, DateTime.UtcNow);

      var isBelowRecommended = status.IsBelowRecommended;
      if (latest is null)
      {
        if (isBelowRecommended)
        {
          await notificationService.NotifyRoslynUpdateAvailable(status.Version, RoslynToolService.MinimumRecommendedVersion, RoslynToolService.MinimumRecommendedVersion, true);
        }
        return;
      }

      if (latest <= currentVersion && !isBelowRecommended)
      {
        logger.LogDebug("No Roslyn tool updates available");
        _cachedRoslynUpdateMessage = null;
        return;
      }

      _cachedRoslynUpdateMessage = $"{currentVersion} -> {latest}";
      logger.LogInformation("Roslyn tool update available: {Message}", _cachedRoslynUpdateMessage);

      await notificationService.NotifyRoslynUpdateAvailable(status.Version, latest.ToString(), RoslynToolService.MinimumRecommendedVersion, isBelowRecommended);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to check for Roslyn tool updates");
    }
  }

  private async Task<DateTime?> GetLastCheckTimeAsync(string cacheFile)
  {
    try
    {
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

  private async Task SaveLastCheckTimeAsync(string cacheFile, DateTime checkTime)
  {
    try
    {
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