using System.Collections.Concurrent;
using EasyDotnet.Application.Interfaces;

namespace EasyDotnet.Infrastructure.Editor;

public class EditorProcessManagerService : IEditorProcessManagerService
{
  private readonly ConcurrentDictionary<Guid, TaskCompletionSource<int>> _pendingJobs = new();

  public Guid RegisterJob()
  {
    var jobId = Guid.NewGuid();
    _pendingJobs[jobId] = new TaskCompletionSource<int>();
    return jobId;
  }

  public void CompleteJob(Guid jobId, int exitCode)
  {
    if (_pendingJobs.TryRemove(jobId, out var tcs))
    {
      tcs.SetResult(exitCode);
    }
  }

  public Task<int> WaitForExitAsync(Guid jobId) => _pendingJobs[jobId].Task;
}
