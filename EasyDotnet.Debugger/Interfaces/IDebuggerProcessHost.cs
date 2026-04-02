namespace EasyDotnet.Debugger.Interfaces;

public interface IDebuggerProcessHost : IAsyncDisposable
{
  int? ProcessId { get; }
  event EventHandler? Exited;

  void Start(string binaryPath);
  Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken);
  Task WaitForExitAsync(CancellationToken cancellationToken);
  void Kill();
}