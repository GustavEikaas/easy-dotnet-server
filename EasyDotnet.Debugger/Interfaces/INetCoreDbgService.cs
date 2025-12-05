using EasyDotnet.Debugger.Messages;

namespace EasyDotnet.Debugger.Interfaces;

public interface INetcoreDbgService
{
  int Start(
      string binaryPath,
      Func<InterceptableAttachRequest, Task<InterceptableAttachRequest>> rewriter,
      bool applyValueConverters,
      Action<Exception> onProcessFailedToStart,
      Func<Task> onDispose);

  Task Completion { get; }
  Task DisposalStarted { get; }

  ValueTask DisposeAsync();
  ValueTask ForceDisposeAsync();
}