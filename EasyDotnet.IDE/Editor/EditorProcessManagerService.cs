using System.Collections.Concurrent;
using EasyDotnet.IDE.Interfaces;

namespace EasyDotnet.IDE.Editor;

public class EditorProcessManagerService : IEditorProcessManagerService
{
  private readonly ConcurrentDictionary<Guid, TaskCompletionSource<int>> _pendingJobs = new();
  private readonly SemaphoreSlim _managedSlot = new(1, 1);

  public bool IsSlotBusy(TerminalSlot slot) => GetSlot(slot).CurrentCount == 0;

  public Guid RegisterJob(TerminalSlot? slot = null)
  {
    if (slot.HasValue && !GetSlot(slot.Value).Wait(0))
    {
      throw new InvalidOperationException($"A job is already running in the {slot.Value} terminal");
    }

    var jobId = Guid.NewGuid();
    _pendingJobs[jobId] = new TaskCompletionSource<int>();
    return jobId;
  }

  public void CompleteJob(Guid jobId, int exitCode)
  {
    if (_pendingJobs.TryGetValue(jobId, out var tcs))
      tcs.TrySetResult(exitCode);
  }

  public void SetFailedToStart(Guid jobId, TerminalSlot? slot, string message)
  {
    if (slot.HasValue)
      GetSlot(slot.Value).Release();
    if (_pendingJobs.TryRemove(jobId, out var tcs))
    {
      tcs.SetException(new InvalidOperationException(message));
    }
  }

  public async Task<int> WaitForExitAsync(Guid jobId, TerminalSlot? slot = null)
  {
    try
    {
      if (!_pendingJobs.TryGetValue(jobId, out var tcs))
        throw new InvalidOperationException($"No pending job registered for {jobId}");
      return await tcs.Task;
    }
    finally
    {
      _pendingJobs.TryRemove(jobId, out _);
      if (slot.HasValue)
        GetSlot(slot.Value).Release();
    }
  }

  private SemaphoreSlim GetSlot(TerminalSlot slot) => slot switch
  {
    TerminalSlot.Managed => _managedSlot,
    _ => throw new ArgumentOutOfRangeException(nameof(slot))
  };
}