namespace EasyDotnet.Debugger.Interfaces;

public interface IDebuggerProcessHost : IAsyncDisposable
{
  Stream StandardInput { get; }
  Stream StandardOutput { get; }
  int? ProcessId { get; }
  event EventHandler? Exited;

  void Start(string binaryPath, IReadOnlyList<string> arguments);
  Task WaitForExitAsync(CancellationToken cancellationToken);
  void Kill();
}