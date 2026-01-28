using System.Text.Json;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class DebugSessionCoordinator(
  ILogger<DebugSessionCoordinator> logger,
  ITcpDebugServer tcpServer,
  IDebuggerProcessHost processHost,
  IDapMessageInterceptor clientInterceptor,
  IDapMessageInterceptor debuggerInterceptor,
  ILogger<DebuggerProxy> proxyLogger,
  ILogger<ProcessMonitor> monitorLogger) : IAsyncDisposable
{
  private DebuggerProxy? _proxy;
  private CancellationTokenSource? _cts;
  private readonly TaskCompletionSource<bool> _completionSource = new();
  private readonly TaskCompletionSource<bool> _disposalStartedSource = new();
  private readonly TaskCompletionSource<bool> _processStartedSource = new();
  private int _isDisposing;
  private Func<Task>? _onDispose;

  private ProcessMonitor? _processMonitor;
  private Task? _telemetryTask;
  private int _eventSeq = 1000;

  public Task ProcessStarted => _processStartedSource.Task;
  public Task Completion => _completionSource.Task;
  public Task DisposalStarted => _disposalStartedSource.Task;
  public int Port => tcpServer.Port;
  public int? ProcessId => processHost?.ProcessId;


  public void StartProcessMonitoring(int systemProcessId)
  {
    if (_processMonitor != null || _telemetryTask != null)
    {
      logger.LogWarning("Process monitoring already started");
      return;
    }

    if (_proxy == null || _cts == null)
    {
      logger.LogWarning("Cannot start monitoring: proxy not ready");
      return;
    }

    logger.LogInformation("Starting process monitoring for PID {ProcessId}", systemProcessId);
    _processMonitor = new ProcessMonitor(systemProcessId, monitorLogger);
    _telemetryTask = StartTelemetryMonitoringAsync(_cts.Token);
  }

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

  private async Task StartTelemetryMonitoringAsync(CancellationToken cancellationToken)
  {
    logger.LogInformation("Starting telemetry monitoring loop");

    try
    {
      while (!cancellationToken.IsCancellationRequested && _proxy != null)
      {
        await Task.Delay(100, cancellationToken);

        if (_processMonitor == null)
          continue;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var cpuUsage = _processMonitor.GetCpuUsage();
        var cpuEvent = new TelemetryEvent
        {
          Seq = Interlocked.Increment(ref _eventSeq),
          Type = "event",
          EventName = "telemetry/cpu",
          Body = JsonSerializer.SerializeToElement(new CpuTelemetryData
          {
            Value = Math.Round(cpuUsage, 2),
            Timestamp = timestamp
          })
        };

        await _proxy.WriteProxyToClientAsync(cpuEvent, cancellationToken);

        var memUsage = _processMonitor.GetMemoryUsage();
        var memEvent = new TelemetryEvent
        {
          Seq = Interlocked.Increment(ref _eventSeq),
          Type = "event",
          EventName = "telemetry/mem",
          Body = JsonSerializer.SerializeToElement(new MemoryTelemetryData
          {
            Value = memUsage,
            Timestamp = timestamp
          })
        };

        await _proxy.WriteProxyToClientAsync(memEvent, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      logger.LogInformation("Telemetry monitoring stopped");
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error in telemetry monitoring loop");
    }
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