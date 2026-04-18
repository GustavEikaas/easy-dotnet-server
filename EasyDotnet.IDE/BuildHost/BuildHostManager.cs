using System.Diagnostics;
using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.BuildHost;

public sealed class BuildHostManager(ILogger<BuildHostManager> logger, BuildHostFactory factory) : IDisposable, IAsyncDisposable, IBuildHostManager
{
  private Process? _serverProcess;
  private JsonRpc? _rpc;
  private int _isDisposed;
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
      InvalidateConnection();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }
  }

  public async IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatchAsync(
      GetProjectPropertiesBatchRequest request,
      [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();
    IAsyncEnumerable<ProjectEvaluationResult> stream;
    try
    {
      stream = await rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<ProjectEvaluationResult>>(
          "project/get-properties-batch",
          request,
          cancellationToken);
    }
    catch (ConnectionLostException)
    {
      InvalidateConnection();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }

    await foreach (var result in stream.WithCancellation(cancellationToken))
    {
      yield return result;
    }
  }

  public async IAsyncEnumerable<BatchBuildResult> BatchBuildAsync(
        BatchBuildRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();
    IAsyncEnumerable<BatchBuildResult> stream;
    try
    {
      stream = await rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<BatchBuildResult>>(
          "projects/batchBuild",
          request,
          cancellationToken);
    }
    catch (ConnectionLostException)
    {
      InvalidateConnection();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }

    await foreach (var result in stream.WithCancellation(cancellationToken))
    {
      yield return result;
    }
  }

  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackagesAsync(
      RestoreRequest request,
      [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();
    IAsyncEnumerable<RestoreResult> stream;
    try
    {
      stream = await rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<RestoreResult>>(
          "projects/restore",
          request,
          cancellationToken);
    }
    catch (ConnectionLostException)
    {
      InvalidateConnection();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }

    await foreach (var result in stream.WithCancellation(cancellationToken))
    {
      yield return result;
    }
  }

  public async Task<ConvertSingleFileResponse> ConvertFileToProjectAsync(string entryPointFilePath, CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();
    try
    {
      return await rpc.InvokeWithParameterObjectAsync<ConvertSingleFileResponse>(
          "singlefile/convert",
          new ConvertSingleFileRequest(entryPointFilePath),
          cancellationToken);
    }
    catch (ConnectionLostException)
    {
      InvalidateConnection();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }
  }

  public async Task<BuildServerDiagnosticsResponse> GetBuildServerDiagnosticsAsync(CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();
    try
    {
      return await rpc.InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver").WaitAsync(cancellationToken);
    }
    catch (ConnectionLostException)
    {
      InvalidateConnection();
      throw new Exception("BuildServer connection was lost. Please try again.");
    }
  }

  public async Task<InstalledPackageReference[]> ListPackageReferencesAsync(string projectPath, CancellationToken cancellationToken)
  {
    EnsureNotDisposed();
    var rpc = await GetRpcClientAsync();
    try
    {
      return await rpc.InvokeWithParameterObjectAsync<InstalledPackageReference[]>(
          "projects/list-package-references",
          new ListPackageReferencesRequest(projectPath),
          cancellationToken);
    }
    catch (ConnectionLostException)
    {
      InvalidateConnection();
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

      InvalidateConnection();

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

  private void InvalidateConnection()
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
  }

  private void EnsureNotDisposed() =>
      ObjectDisposedException.ThrowIf(_isDisposed == 1, this);

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
    _connectionLock.Dispose();
    InvalidateConnection();
  }

  public ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return ValueTask.CompletedTask;
    _connectionLock.Dispose();
    InvalidateConnection();
    return ValueTask.CompletedTask;
  }
}