using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.IDE.Types;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class StandardAttachStrategy(ILogger<StandardAttachStrategy> logger, int pid, string? cwd = null) : IDebugSessionStrategy
{
  private readonly TaskCompletionSource<int> _processIdTcs = new();

  public Task PrepareAsync(CancellationToken ct)
  {
    logger.LogInformation("Preparing PID attach for PID {Pid}", pid);
    return Task.CompletedTask;
  }

  public Task TransformRequestAsync(InterceptableAttachRequest request, IDebuggerProxy proxy)
  {
    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.ProcessId = pid;
    if (cwd is not null)
      request.Arguments.Cwd = cwd;

    return Task.CompletedTask;
  }

  public void OnDebugSessionReady(DebugSession debugSession, IDebuggerProxy proxy)
  {

  }

  public Task<int>? GetProcessIdAsync() => _processIdTcs.Task;

  public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}