using System.Diagnostics;
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
  ILogger<DebuggerProxy> proxyLogger) : IAsyncDisposable
{
  public DebuggerProxy? Proxy;
  private CancellationTokenSource? _cts;
  private readonly TaskCompletionSource<bool> _completionSource = new();
  private readonly TaskCompletionSource<bool> _disposalStartedSource = new();
  private readonly TaskCompletionSource<bool> _processStartedSource = new();
  private readonly TaskCompletionSource<int?> _debugeeProcessStartedSource = new();
  private readonly TaskCompletionSource _configurationDoneSource = new();
  private int _isDisposing;
  private Func<Task>? _onDispose;

  public Task ProcessStarted => _processStartedSource.Task;
  public Task DebugeeProcessStarted => _debugeeProcessStartedSource.Task;
  public Task ConfigurationDone => _configurationDoneSource.Task;
  public Task Completion => _completionSource.Task;
  public Task DisposalStarted => _disposalStartedSource.Task;
  public int Port => tcpServer.Port;
  public int? ProcessId => processHost?.ProcessId;

  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

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

        Proxy = new DebuggerProxy(clientDap, debuggerDap, proxyLogger);
        Proxy.Start(_cts.Token, async () => await TriggerCleanupAsync());

        logger.LogInformation("Debug session ready");

        await Proxy.Completion;
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
    _debugeeProcessStartedSource.SetResult(processId);

    StartTelemetryMonitoring(processId);
  }

  public void NotifyConfigurationDone()
  {
    if (_configurationDoneSource.Task.IsCompleted)
    {
      throw new Exception("ConfigurationDone already called");
    }

    logger.LogInformation("Configuration done event reported");
    _configurationDoneSource.SetResult();
  }

  private void StartTelemetryMonitoring(int processId)
  {
    if (_cts is null)
    {
      return;
    }
    var token = _cts.Token;

    _ = Task.Run(async () =>
    {
      try
      {
        using var process = Process.GetProcessById(processId);
        var lastCpuTime = process.TotalProcessorTime;
        var lastCheckTime = DateTime.UtcNow;

        while (!token.IsCancellationRequested && !process.HasExited)
        {
          await Task.Delay(100, token);

          if (Proxy is null)
          {
            logger.LogDebug("Proxy is null, stopping telemetry");
            break;
          }

          try
          {
            process.Refresh();

            var currentTime = DateTime.UtcNow;
            var currentCpuTime = process.TotalProcessorTime;

            var cpuElapsed = (currentCpuTime - lastCpuTime).TotalMilliseconds;
            var timeElapsed = (currentTime - lastCheckTime).TotalMilliseconds;
            var cpuUsage = Math.Min(100, (int)(cpuElapsed / (timeElapsed * Environment.ProcessorCount) * 100));

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lastCpuTime = currentCpuTime;
            lastCheckTime = currentTime;

            await Proxy.EmitEventToClientAsync(new TelemetryEvent
            {
              Seq = 0,
              Type = "event",
              EventName = "telemetry/metrics",
              Body = JsonSerializer.SerializeToElement(new Metrics()
              {
                CpuPercent = cpuUsage,
                MemoryBytes = process.WorkingSet64,
                Timestamp = timestamp,
              }, SerializerOptions)
            }, token);
          }
          catch (InvalidOperationException)
          {
            logger.LogInformation("Debugee process exited, stopping telemetry");
            break;
          }
        }
      }
      catch (OperationCanceledException)
      {
        logger.LogDebug("Telemetry monitoring canceled");
      }
      catch (ArgumentException)
      {
        logger.LogWarning("Could not find process {processId} for telemetry", processId);
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error in telemetry monitoring");
      }
    }, token);
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