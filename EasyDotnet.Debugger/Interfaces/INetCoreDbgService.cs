using EasyDotnet.Debugger.Messages;

namespace EasyDotnet.Debugger.Interfaces;

public interface INetcoreDbgService
{
  Task Completion { get; }

  ValueTask DisposeAsync();
  Task<int> Start(
    string binaryPath,
    Func<InterceptableAttachRequest, Task<InterceptableAttachRequest>> rewriter,
    bool applyValueConverters,
    Action<Exception> onProcessFailedToStart,
    Action onDispose);
}