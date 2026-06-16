using System.Diagnostics;
using EasyDotnet.Aspire.Contracts;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Aspire;

/// <summary>
/// Owns the lifetime of the spawned Aspire host process + its RPC connection,
/// spawning it on demand (mirrors <see cref="BuildHost.BuildHostManager"/>).
/// </summary>
public sealed class AspireHostManager(ILogger<AspireHostManager> logger, AspireHostFactory factory) : IAsyncDisposable
{
  private Process? _process;
  private JsonRpc? _rpc;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);

  public async Task<LaunchAppHostResponse> LaunchAsync(string appHostProjectPath, bool debug, CancellationToken ct)
  {
    var rpc = await GetRpcClientAsync();
    return await rpc.InvokeWithParameterObjectAsync<LaunchAppHostResponse>(
        AspireRpcMethods.Launch, new LaunchAppHostRequest(appHostProjectPath, debug), ct);
  }

  public async Task ShutdownAsync(CancellationToken ct = default)
  {
    if (_rpc?.IsDisposed != false || _process?.HasExited != false)
    {
      return;
    }
    try
    {
      await _rpc.InvokeAsync(AspireRpcMethods.Shutdown).WaitAsync(ct);
    }
    catch (ConnectionLostException) { }
    finally
    {
      InvalidateConnection();
    }
  }

  private async Task<JsonRpc> GetRpcClientAsync()
  {
    if (_rpc?.IsDisposed == false && _process?.HasExited == false)
    {
      return _rpc;
    }

    await _connectionLock.WaitAsync();
    try
    {
      if (_rpc?.IsDisposed == false && _process?.HasExited == false)
      {
        return _rpc;
      }

      InvalidateConnection();

      logger.LogInformation("Spawning new Aspire host instance...");
      var (process, rpc) = await factory.StartServerAsync();
      _process = process;
      _rpc = rpc;
      _rpc.Disconnected += (_, e) => logger.LogInformation("Aspire host disconnected: {Reason}", e.Reason);
      return _rpc;
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  private void InvalidateConnection()
  {
    try { _rpc?.Dispose(); } catch { }
    try
    {
      if (_process?.HasExited == false)
      {
        _process.Kill();
      }
    }
    catch { }

    _process = null;
    _rpc = null;
  }

  public ValueTask DisposeAsync()
  {
    _connectionLock.Dispose();
    InvalidateConnection();
    return ValueTask.CompletedTask;
  }
}