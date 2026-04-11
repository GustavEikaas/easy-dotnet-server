using StreamJsonRpc;

namespace EasyDotnet.IDE.AppWrapper;

public class AppWrapperEntry(int pid, JsonRpc rpc)
{
  public int Pid { get; } = pid;
  public JsonRpc Rpc { get; } = rpc;

  // 0 = Idle, 1 = Running.
  private int _state;

  private Guid? _currentJobId;

  public bool TrySetRunning() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

  public void SetIdle() => Volatile.Write(ref _state, 0);

  public Guid? CurrentJobId => _currentJobId;

  public void SetJob(Guid jobId) => _currentJobId = jobId;

  public void ClearJob() => _currentJobId = null;
}