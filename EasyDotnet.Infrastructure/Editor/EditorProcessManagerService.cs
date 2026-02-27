using System.Collections.Concurrent;
using EasyDotnet.Application.Interfaces;

namespace EasyDotnet.Infrastructure.Editor;

public class EditorProcessManagerService : IEditorProcessManagerService
{
  private readonly ConcurrentDictionary<Guid, TaskCompletionSource<int>> _pendingJobs = new();
  private readonly SemaphoreSlim _managedSlot = new(1, 1);
  private readonly SemaphoreSlim _longRunningSlot = new(1, 1);

  public bool IsSlotBusy(TerminalSlot slot) => GetSlot(slot).CurrentCount == 0;

  public Guid RegisterJob(TerminalSlot slot)
  {
    if (!GetSlot(slot).Wait(0))
    {
      throw new InvalidOperationException($"A job is already running in the {slot} terminal");
    }

    var jobId = Guid.NewGuid();
    _pendingJobs[jobId] = new TaskCompletionSource<int>();
    return jobId;
  }

  public void CompleteJob(Guid jobId, int exitCode)
  {
    if (_pendingJobs.TryRemove(jobId, out var tcs))
      tcs.SetResult(exitCode);
  }

  public void SetFailedToStart(Guid jobId, TerminalSlot slot, string message)
  {
    GetSlot(slot).Release();
    if (_pendingJobs.TryRemove(jobId, out var tcs))
    {
      tcs.SetException(new InvalidOperationException(message));
    }
  }

  public async Task<int> WaitForExitAsync(Guid jobId, TerminalSlot slot)
  {
    try
    {
      return await _pendingJobs[jobId].Task;
    }
    finally
    {
      GetSlot(slot).Release();
    }
  }

  private SemaphoreSlim GetSlot(TerminalSlot slot) => slot switch
  {
    TerminalSlot.Managed => _managedSlot,
    TerminalSlot.LongRunning => _longRunningSlot,
    _ => throw new ArgumentOutOfRangeException(nameof(slot))
  };
}