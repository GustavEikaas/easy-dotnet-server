using System;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Notifications;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Services;

public sealed record ServerRestoreRequest(string TargetPath);
//UpdateType is "major" | "minor" | "patch"
public sealed record ServerUpdateAvailable(Version CurrentVersion, Version AvailableVersion, string UpdateType);
public sealed record DisplayMessage(string Message);
public sealed record ProjectChangedNotification(string ProjectPath, string? TargetFrameworkMoniker = null, string? Configuration = null);

public class NotificationService(JsonRpc jsonRpc) : INotificationService
{
  [RpcNotification("request/restore")]
  public async Task RequestRestoreAsync(string targetPath) => await jsonRpc.NotifyWithParameterObjectAsync("request/restore", new ServerRestoreRequest(targetPath));

  [RpcNotification("_server/update-available")]
  public async Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType) => await jsonRpc.NotifyWithParameterObjectAsync("_server/update-available", new ServerUpdateAvailable(currentVersion, availableVersion, updateType));

  [RpcNotification("project/changed")]
  public async Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug") => await jsonRpc.NotifyWithParameterObjectAsync("project/changed", new ProjectChangedNotification(projectPath, targetFrameworkMoniker, configuration));

  [RpcNotification("displayError")]
  public async Task DisplayError(string message) =>
      await jsonRpc.NotifyWithParameterObjectAsync("displayError", new DisplayMessage(message));

  [RpcNotification("displayWarning")]
  public async Task DisplayWarning(string message) =>
    await jsonRpc.NotifyWithParameterObjectAsync("displayWarning", new DisplayMessage(message));

  [RpcNotification("displayMessage")]
  public async Task DisplayMessage(string message) =>
    await jsonRpc.NotifyWithParameterObjectAsync("displayMessage", new DisplayMessage(message));
}