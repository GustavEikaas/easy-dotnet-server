using StreamJsonRpc;

namespace EasyDotnet.IDE.AppWrapper;

public class AppWrapperEntry(int pid, JsonRpc rpc)
{
  public int Pid { get; } = pid;
  public JsonRpc Rpc { get; } = rpc;

  // 0 = Idle, 1 = Running.
  private int _state;

  public bool TrySetRunning() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

  public void SetIdle() => Volatile.Write(ref _state, 0);
}