using EasyDotnet.Debugger.Session;

namespace EasyDotnet.Debugger;

public class DebugSession : IAsyncDisposable
{
  private readonly DebugSessionCoordinator _coordinator;
  private int _stoppedRaised;

  public event Action? Stopped;

  public Task Completion => _coordinator.Completion;
  public Task DisposalStarted => _coordinator.DisposalStarted;
  public Task StoppedTask => _coordinator.DisposalStarted;

  public Task ProcessStarted => _coordinator.ProcessStarted;
  public Task DebugeeProcessStarted => _coordinator.DebugeeProcessStarted;
  public int? ProcessId => _coordinator.ProcessId;
  public int Port => _coordinator.Port;

  internal DebugSession(DebugSessionCoordinator coordinator)
  {
    _coordinator = coordinator;
    _ = _coordinator.DisposalStarted.ContinueWith(
      _ =>
      {
        if (Interlocked.Exchange(ref _stoppedRaised, 1) == 1) return;
        Stopped?.Invoke();
      },
      TaskScheduler.Default);
  }

  public void Start(
    string binaryPath,
    Action<Exception> onProcessFailedToStart,
    Func<Task> onDispose,
    CancellationToken cancellationToken) => _coordinator.Start(binaryPath, onProcessFailedToStart, onDispose, cancellationToken);
  public void NotifyDebugeeProcessStarted(int processId) => _coordinator.NotifyDebugeeProcessStarted(processId);
  public async ValueTask DisposeAsync() => await _coordinator.DisposeAsync();
  public async ValueTask ForceDisposeAsync() => await _coordinator.ForceDisposeAsync();
  public async Task<DebuggerProxy> WaitForConfigurationDoneAsync()
  {
    await _coordinator.ConfigurationDone;
    return _coordinator.Proxy!;
  }
}