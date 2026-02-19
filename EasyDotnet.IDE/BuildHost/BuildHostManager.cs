using System.Diagnostics;
using EasyDotnet.BuildServer.Contracts;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.BuildHost;

public sealed class BuildHostManager(ILogger<BuildHostManager> logger, BuildHostFactory factory) : IDisposable, IAsyncDisposable, IBuildHostManager
{
  private Process? _serverProcess;
  private JsonRpc? _rpc;
  private bool _isDisposed;

  private readonly SemaphoreSlim _connectionLock = new(1, 1);

  public async Task<GetWatchListResponse> GetProjectWatchListAsync(GetWatchListRequest request, CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();
    try
    {
      return await rpc.InvokeWithParameterObjectAsync<GetWatchListResponse>("project/get-watchlist", request, cancellationToken);
    }
    catch (ConnectionLostException)
    {
      await InvalidateConnectionAsync();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }
  }

  public async Task<IAsyncEnumerable<ProjectEvaluationResult>> GetProjectPropertiesBatchAsync(
      GetProjectPropertiesBatchRequest request,
      CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();

    try
    {
      return await rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<ProjectEvaluationResult>>(
          "project/get-properties-batch",
          request,
          cancellationToken);
    }
    catch (ConnectionLostException)
    {
      await InvalidateConnectionAsync();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }
  }

  public async Task<IAsyncEnumerable<RestoreResult>> RestoreNugetPackagesAsync(
      RestoreRequest request,
      CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();
    try
    {
      return await rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<RestoreResult>>(
          "projects/restore",
          request,
          cancellationToken);
    }
    catch (ConnectionLostException)
    {
      await InvalidateConnectionAsync();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }
  }

  private async Task<JsonRpc> GetRpcClientAsync()
  {
    if (_rpc?.IsDisposed == false && _serverProcess?.HasExited == false)
    {
      return _rpc;
    }

    await _connectionLock.WaitAsync();
    try
    {
      if (_rpc?.IsDisposed == false && _serverProcess?.HasExited == false)
      {
        return _rpc;
      }

      await InvalidateConnectionAsync();

      logger.LogInformation("Spawning new BuildServer instance...");
      var (process, rpc) = await factory.StartServerAsync();

      _serverProcess = process;
      _rpc = rpc;

      _rpc.Disconnected += (s, e) => logger.LogInformation("BuildHost disconnected: {Reason}", e.Reason);

      return _rpc;
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  private async Task InvalidateConnectionAsync()
  {
    try { _rpc?.Dispose(); } catch { }
    try
    {
      if (_serverProcess?.HasExited == false)
        _serverProcess.Kill();
    }
    catch { }

    _serverProcess = null;
    _rpc = null;
    await Task.CompletedTask;
  }

  private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;
    _connectionLock.Dispose();
    InvalidateConnectionAsync().GetAwaiter().GetResult();
  }

  public async ValueTask DisposeAsync()
  {
    if (_isDisposed) return;
    _isDisposed = true;
    _connectionLock.Dispose();
    await InvalidateConnectionAsync();
  }
}
