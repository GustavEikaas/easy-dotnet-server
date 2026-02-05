using EasyDotnet.Debugger.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class DebugSessionCoordinator(
  ILogger<DebugSessionCoordinator> logger,
  ITcpDebugServer tcpServer,
  IDebuggerProcessHost processHost,
  IDapMessageInterceptor clientInterceptor,
  IDapMessageInterceptor debuggerInterceptor,
  ILogger<DebuggerProxy> proxyLogger) : IAsyncDisposable
{
  private DebuggerProxy? _proxy;
  private CancellationTokenSource? _cts;
  private readonly TaskCompletionSource<bool> _completionSource = new();
  private readonly TaskCompletionSource<bool> _disposalStartedSource = new();
  private readonly TaskCompletionSource<bool> _processStartedSource = new();
  private readonly TaskCompletionSource<bool> _debugeeProcessStartedSource = new();
  private int _isDisposing;
  private Func<Task>? _onDispose;

  public Task ProcessStarted => _processStartedSource.Task;
  public Task DebugeeProcessStarted => _debugeeProcessStartedSource.Task;
  public Task Completion => _completionSource.Task;
  public Task DisposalStarted => _disposalStartedSource.Task;
  public int Port => tcpServer.Port;
  public int? ProcessId => processHost?.ProcessId;

  public void Start(
   string debuggerBinaryPath,
   Action<Exception> onProcessFailedToStart,
   Func<Task> onDispose,
   CancellationToken cancellationToken)
  {
    _onDispose = onDispose;
    _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    _ = Task.Run(async () =>
    {
      try
      {
        Stream stream;
        try
        {
          stream = await tcpServer.AcceptClientAsync(TimeSpan.FromSeconds(30), _cts.Token);
        }
        catch (TimeoutException)
        {
          logger.LogWarning("No client connected within 30 seconds");
          _completionSource.SetCanceled();
          return;
        }

        processHost.Exited += async (_, _) => await TriggerCleanupAsync();

        try
        {
          processHost.Start(debuggerBinaryPath, "--interpreter=vscode");
          _processStartedSource.SetResult(true);
          logger.LogInformation("Debugger process started successfully");
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Failed to start debugger process");
          _processStartedSource.SetException(ex);
          onProcessFailedToStart(ex);
          await TriggerCleanupAsync();
          throw;
        }

        var clientDap = new Client(stream, stream,
          async (msg, proxy) => await clientInterceptor.InterceptAsync(msg, proxy, CancellationToken.None));

        var debuggerDap = new Debugger(processHost.StandardInput, processHost.StandardOutput,
          async (msg, proxy) => await debuggerInterceptor.InterceptAsync(msg, proxy, CancellationToken.None));

        _proxy = new DebuggerProxy(clientDap, debuggerDap, proxyLogger);
        _proxy.Start(_cts.Token, async () => await TriggerCleanupAsync());

        logger.LogInformation("Debug session ready");

        await _proxy.Completion;
        _completionSource.SetResult(true);
      }
      catch (OperationCanceledException)
      {
        logger.LogInformation("Session was canceled");
        _completionSource.SetCanceled();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Unhandled exception in debug session");
        _completionSource.SetException(ex);
      }
    }, cancellationToken);
  }

  public void NotifyDebugeeProcessStarted(int processId)
  {
    if (_debugeeProcessStartedSource.Task.IsCompleted)
    {
      logger.LogDebug("DebugeeProcessStarted already completed, ignoring processId: {processId}", processId);
      return;
    }

    logger.LogInformation("Debugee process started: {processId}", processId);
    _debugeeProcessStartedSource.SetResult(true);
  }

  private async Task TriggerCleanupAsync()
  {
    logger.LogDebug("Cleanup triggered");
    await DisposeAsync();
  }

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.CompareExchange(ref _isDisposing, 1, 0) == 1)
    {
      logger.LogDebug("Already disposing");
      return;
    }

    _disposalStartedSource.TrySetResult(true);
    _processStartedSource.TrySetCanceled();
    logger.LogInformation("Beginning shutdown");

    _cts?.Cancel();

    await processHost.DisposeAsync();
    await tcpServer.DisposeAsync();
    _cts?.Dispose();

    if (_onDispose != null)
    {
      try
      {
        await _onDispose();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error in onDispose callback");
      }
    }

    logger.LogInformation("Shutdown complete");
  }

  public async ValueTask ForceDisposeAsync()
  {
    if (Interlocked.CompareExchange(ref _isDisposing, 1, 0) == 1)
    {
      logger.LogDebug("Already disposing, waiting");
      await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(2)), Completion);
      return;
    }

    _disposalStartedSource.TrySetResult(true);
    logger.LogInformation("Force disposal");

    _cts?.Cancel();
    processHost.Kill();
    tcpServer.Stop();

    if (_onDispose != null)
    {
      await _onDispose();
    }

    logger.LogInformation("Force disposal complete");
  }
}