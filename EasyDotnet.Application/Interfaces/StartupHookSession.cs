using System.IO.Pipes;

namespace EasyDotnet.Application.Interfaces;

public sealed class StartupHookSession(
    NamedPipeServerStream pipe,
    Dictionary<string, string> environmentVariables) : IAsyncDisposable
{
  public Dictionary<string, string> EnvironmentVariables { get; } = environmentVariables;

  public async Task<int> WaitForPidAsync(CancellationToken ct = default)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));

    await pipe.WaitForConnectionAsync(timeout.Token);
    var buf = new byte[4];
    await pipe.ReadExactlyAsync(buf, timeout.Token);
    return BitConverter.ToInt32(buf, 0);
  }

  public void Resume()
  {
    if (!pipe.IsConnected)
      throw new InvalidOperationException("StartupHook is not connected, cannot resume");
    pipe.WriteByte(1);
    pipe.Flush();
  }

  public async ValueTask DisposeAsync() => await pipe.DisposeAsync();
}