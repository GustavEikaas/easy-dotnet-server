using EasyDotnet.Debugger.Session;

namespace EasyDotnet.Debugger;

public class DebugSession : IAsyncDisposable
{
  private readonly DebugSessionCoordinator _coordinator;

  public Task Completion => _coordinator.Completion;
  public Task DisposalStarted => _coordinator.DisposalStarted;
  public int Port => _coordinator.Port;

  internal DebugSession(DebugSessionCoordinator coordinator) => _coordinator = coordinator;

  public void Start(
    string binaryPath,
    Action<Exception> onProcessFailedToStart,
    Func<Task> onDispose,
    CancellationToken cancellationToken) => _coordinator.Start(binaryPath, onProcessFailedToStart, onDispose, cancellationToken);

  public async ValueTask DisposeAsync() => await _coordinator.DisposeAsync();
  public async ValueTask ForceDisposeAsync() => await _coordinator.ForceDisposeAsync();
}