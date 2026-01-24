namespace EasyDotnet.Debugger.Interfaces;

public interface ITcpDebugServer : IAsyncDisposable
{
  int Port { get; }
  Task<Stream> AcceptClientAsync(TimeSpan timeout, CancellationToken cancellationToken);
  void Stop();
}