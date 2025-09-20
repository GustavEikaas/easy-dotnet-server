using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Notifications;
using StreamJsonRpc;

namespace EasyDotnet.Services;

public sealed record ServerRestoreRequest(string TargetPath);
public sealed record ProjectChangedNotification(string ProjectPath, string? TargetFrameworkMoniker = null, string? Configuration = null);

public class NotificationService(JsonRpc jsonRpc) : INotificationService
{
  [RpcNotification("request/restore")]
  public async Task RequestRestoreAsync(string targetPath) => await jsonRpc.NotifyWithParameterObjectAsync("request/restore", new ServerRestoreRequest(targetPath));

  [RpcNotification("project/changed")]
  public async Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug") => await jsonRpc.NotifyWithParameterObjectAsync("project/changed", new ProjectChangedNotification(projectPath, targetFrameworkMoniker, configuration));
}