using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Messages;

namespace EasyDotnet.IDE.Types;

public interface IDebugSessionStrategy : IAsyncDisposable
{
  Task PrepareAsync(CancellationToken ct);

  Task TransformRequestAsync(InterceptableAttachRequest request, IDebuggerProxy proxy);

  Task<int>? GetProcessIdAsync();

  void OnDebugSessionReady(DebugSession debugSession, IDebuggerProxy proxy);
}