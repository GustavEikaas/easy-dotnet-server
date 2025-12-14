namespace EasyDotnet.Debugger.Interfaces;

public interface IDebuggerProcessHost : IAsyncDisposable
{
  Stream StandardInput { get; }
  Stream StandardOutput { get; }
  int? ProcessId { get; }
  event EventHandler? Exited;

  void Start(string binaryPath, string arguments);
  Task WaitForExitAsync(CancellationToken cancellationToken);
  void Kill();
}