using EasyDotnet.Application.Interfaces;
using EasyDotnet.AppWrapper.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.IDE.AppWrapper;

public class AppWrapperConnectionHandler(
    AppWrapperManager manager,
    IEditorProcessManagerService editorProcessManagerService,
    JsonRpc rpc)
{
  private AppWrapperEntry? _entry;

  [JsonRpcMethod("appWrapper/initialize", UseSingleObjectParameterDeserialization = true)]
  public Task InitializeAsync(AppWrapperInitInfo info)
  {
    _entry = manager.Register(info, rpc);
    return Task.CompletedTask;
  }

  [JsonRpcMethod("appWrapper/exited", UseSingleObjectParameterDeserialization = true)]
  public Task ExitedAsync(AppExitedNotification notification)
  {
    _entry?.SetIdle();
    editorProcessManagerService.CompleteJob(notification.JobId, notification.ExitCode);
    return Task.CompletedTask;
  }
}