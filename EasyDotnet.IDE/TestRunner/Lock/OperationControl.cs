using System.Collections.Concurrent;

namespace EasyDotnet.IDE.TestRunner.Lock;

/// <summary>
/// Per-operation hook registry so the service can escalate from cancel (CTS) to kill (hard stop).
/// Adapters register kill actions for any external resources (processes, wrappers, sockets).
/// </summary>
public sealed class OperationControl
{
  private readonly ConcurrentBag<Func<Task>> _killActions = new();
  private int _killed;

  public void RegisterKill(Func<Task> killAction)
  {
    if (Volatile.Read(ref _killed) == 1)
    {
      _ = TryRunAsync(killAction);
      return;
    }

    _killActions.Add(killAction);
  }

  public async Task KillAsync(TimeSpan? timeout = null)
  {
    if (Interlocked.Exchange(ref _killed, 1) == 1) return;

    var actions = _killActions.ToArray();
    if (actions.Length == 0) return;

    var killTask = Task.WhenAll(actions.Select(TryRunAsync));

    if (timeout is { } t)
    {
      await Task.WhenAny(killTask, Task.Delay(t));
      return;
    }

    await killTask;
  }

  private static async Task TryRunAsync(Func<Task> action)
  {
    try { await action(); }
    catch { /* best effort */ }
  }
}