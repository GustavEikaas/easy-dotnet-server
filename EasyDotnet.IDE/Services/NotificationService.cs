using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Notifications;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Services;

//UpdateType is "major" | "minor" | "patch"
public sealed record ServerUpdateAvailable(Version CurrentVersion, Version AvailableVersion, string UpdateType);
public sealed record ProjectChangedNotification(string ProjectPath, string? TargetFrameworkMoniker = null, string? Configuration = null);
public sealed record ActiveProjectChangedNotification(string? ProjectPath, string? ProjectName, string? LaunchProfile);

public class NotificationService(JsonRpc jsonRpc) : INotificationService
{
  [RpcNotification("_server/update-available")]
  public async Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType) => await jsonRpc.NotifyWithParameterObjectAsync("_server/update-available", new ServerUpdateAvailable(currentVersion, availableVersion, updateType));

  [RpcNotification("project/changed")]
  public async Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug") => await jsonRpc.NotifyWithParameterObjectAsync("project/changed", new ProjectChangedNotification(projectPath, targetFrameworkMoniker, configuration));

  [RpcNotification("activeProject/changed")]
  public async Task NotifyActiveProjectChanged(string? projectPath, string? projectName, string? launchProfile) => await jsonRpc.NotifyWithParameterObjectAsync("activeProject/changed", new ActiveProjectChangedNotification(projectPath, projectName, launchProfile));
}