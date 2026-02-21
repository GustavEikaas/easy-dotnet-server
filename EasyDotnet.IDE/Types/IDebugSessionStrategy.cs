using EasyDotnet.Debugger.Messages;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.Types;

public interface IDebugSessionStrategy : IAsyncDisposable
{
  Task PrepareAsync(DotnetProject project, CancellationToken ct);

  Task TransformRequestAsync(InterceptableAttachRequest request);

  Task<int>? GetProcessIdAsync();

  void OnDebugSessionReady(Debugger.DebugSession debugSession);
}