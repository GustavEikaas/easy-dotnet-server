using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.Mcp.Headless;

/// <summary>Drops editor notifications; a headless host has nobody to tell.</summary>
public sealed class NullNotificationService : INotificationService
{
  public Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug") => Task.CompletedTask;
  public Task NotifySolutionProjectsLoaded() => Task.CompletedTask;
  public Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType) => Task.CompletedTask;
  public Task NotifyRoslynUpdateAvailable(string? currentVersion, string availableVersion, string minimumRecommendedVersion, bool isBelowRecommended) => Task.CompletedTask;
  public Task NotifyActiveProjectChanged(string? projectPath, string? projectName, string? launchProfile) => Task.CompletedTask;
  public Task NotifyRunningProcessesChangedAsync(RunningSessionInfo[] projects) => Task.CompletedTask;
}