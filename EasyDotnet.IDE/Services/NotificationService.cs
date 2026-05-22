using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Notifications;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Services;

//UpdateType is "major" | "minor" | "patch"
public sealed record ServerUpdateAvailable(Version CurrentVersion, Version AvailableVersion, string UpdateType);
public sealed record RoslynUpdateAvailable(string? CurrentVersion, string AvailableVersion, string MinimumRecommendedVersion, bool IsBelowRecommended);
public sealed record ProjectChangedNotification(string ProjectPath, string? TargetFrameworkMoniker = null, string? Configuration = null);
public sealed record ActiveProjectChangedNotification(string? ProjectPath, string? ProjectName, string? LaunchProfile);
public sealed record RunningProcessesChangedNotification(RunningSessionInfo[] Projects);
public sealed record SolutionProjectsLoadedNotification();
public sealed record RunningSessionInfo(string Name, bool IsDebugging);

public class NotificationService(JsonRpc jsonRpc) : INotificationService
{
  [RpcNotification("_server/update-available")]
  public async Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType) => await jsonRpc.NotifyWithParameterObjectAsync("_server/update-available", new ServerUpdateAvailable(currentVersion, availableVersion, updateType));

  [RpcNotification("roslyn/update-available")]
  public async Task NotifyRoslynUpdateAvailable(string? currentVersion, string availableVersion, string minimumRecommendedVersion, bool isBelowRecommended) =>
      await jsonRpc.NotifyWithParameterObjectAsync("roslyn/update-available", new RoslynUpdateAvailable(currentVersion, availableVersion, minimumRecommendedVersion, isBelowRecommended));

  [RpcNotification("project/changed")]
  public async Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug") => await jsonRpc.NotifyWithParameterObjectAsync("project/changed", new ProjectChangedNotification(projectPath, targetFrameworkMoniker, configuration));

  [RpcNotification("solution/projects-loaded")]
  public async Task NotifySolutionProjectsLoaded() => await jsonRpc.NotifyWithParameterObjectAsync("solution/projects-loaded", new SolutionProjectsLoadedNotification());

  [RpcNotification("activeProject/changed")]
  public async Task NotifyActiveProjectChanged(string? projectPath, string? projectName, string? launchProfile) => await jsonRpc.NotifyWithParameterObjectAsync("activeProject/changed", new ActiveProjectChangedNotification(projectPath, projectName, launchProfile));

  [RpcNotification("runningProcesses/changed")]
  public async Task NotifyRunningProcessesChangedAsync(RunningSessionInfo[] projects) => await jsonRpc.NotifyWithParameterObjectAsync("runningProcesses/changed", new RunningProcessesChangedNotification(projects));
}
