using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Services;

public class NetcoreDbgService(
  ILogger<NetcoreDbgService> logger,
  ILogger<DebuggerProxy> debuggerProxyLogger,
  ValueConverterService valueConverterService) : INetcoreDbgService
{
  private static readonly JsonSerializerOptions LoggingSerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

  private int _isDisposing;
  private int _cleanupTriggered;

  private readonly TaskCompletionSource<bool> _completionSource = new();
  private readonly TaskCompletionSource<bool> _disposalStartedSource = new();
  private CancellationTokenSource? _cancellationTokenSource;
  private Task? _sessionTask;
  private TcpListener? _listener;
  private Process? _process;
  private TcpClient? _client;
  private DebuggerProxy? _proxy;
  private Func<Task>? _onDispose;

  public Task Completion => _completionSource.Task;
  public Task DisposalStarted => _disposalStartedSource.Task;

  public int Start(
    string binaryPath,
    Func<InterceptableAttachRequest, Task<InterceptableAttachRequest>> rewriter,
    bool applyValueConverters,
    Action<Exception> onProcessFailedToStart,
    Func<Task> onDispose)
  {
    _onDispose = onDispose;
    _cancellationTokenSource = new CancellationTokenSource();

    _listener = new TcpListener(IPAddress.Any, 0);
    _listener.Start();
    var assignedPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
    logger.LogInformation("Listening for client on port {port}.", assignedPort);

    _sessionTask = Task.Run(async () =>
    {
      try
      {
        try
        {
          logger.LogInformation("Waiting for client...");
          _client = await _listener.AcceptTcpClientAsync().WaitAsync(
            TimeSpan.FromSeconds(30),
            _cancellationTokenSource.Token);
          logger.LogInformation("Client connected.");
        }
        catch (TimeoutException)
        {
          logger.LogWarning("No client connected within 30 seconds. Triggering cleanup.");
          await TriggerCleanup();
          _completionSource.SetCanceled();
          return;
        }

        var tcpStream = _client.GetStream();

        var clientDap = new Client(tcpStream, tcpStream, async (msg, proxy) =>
        {
          try
          {
            switch (msg)
            {
              case InterceptableAttachRequest attachReq:
                var modified = await rewriter(attachReq);
                logger.LogInformation(
                  "[TCP] Intercepted attach request: {modified}",
                  JsonSerializer.Serialize(modified, LoggingSerializerOptions));
                return modified;

              case InterceptableVariablesRequest varRequest:
                if (varRequest.Arguments?.VariablesReference is not null)
                {
                  var converter = valueConverterService.TryGetConverterFor(
                    varRequest.Arguments.VariablesReference);
                  if (converter is not null)
                  {
                    var result = await converter.TryConvertAsync(
                      varRequest.Arguments.VariablesReference,
                      proxy,
                      CancellationToken.None);
                    var x = proxy.GetAndRemoveContext(varRequest.Seq)
                      ?? throw new Exception("Proxy request not found");
                    result.RequestSeq = x.OriginalSeq;
                    await proxy.WriteProxyToClientAsync(result, CancellationToken.None);
                    return null;
                  }
                }
                logger.LogInformation(
                  "[TCP] Intercepted variables request: {modified}",
                  JsonSerializer.Serialize(varRequest, LoggingSerializerOptions));
                return varRequest;

              case SetBreakpointsRequest setBpReq:
                if (OperatingSystem.IsWindows())
                {
                  logger.LogInformation(
                    "[TCP] Intercepted set breakpoints request: Normalizing path separators");
                  setBpReq.Arguments.Source.Path = setBpReq.Arguments.Source.Path.Replace('/', '\\');
                }

                logger.LogInformation(
                  "[TCP] setBreakpoints request: {message}",
                  JsonSerializer.Serialize(setBpReq, LoggingSerializerOptions));
                return setBpReq;

              case Request req:
                logger.LogInformation(
                  "[TCP] request: {message}",
                  JsonSerializer.Serialize(req, LoggingSerializerOptions));
                return req;

              default:
                throw new Exception($"Unsupported DAP message from client: {msg}");
            }
          }
          catch (Exception e)
          {
            logger.LogError(e, "Exception in client DAP handler");
            throw;
          }
        });

        _process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = binaryPath,
            Arguments = "--interpreter=vscode",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
          },
          EnableRaisingEvents = true,
        };

        _process.Exited += async (sender, args) =>
        {
          logger.LogDebug("Debugger process exited.");
          await TriggerCleanup();
        };

        try
        {
          _process.Start();
        }
        catch (Exception e)
        {
          onProcessFailedToStart(e);
          await TriggerCleanup();
          throw;
        }

        var debuggerDap = new Debugger(
          _process.StandardInput.BaseStream,
          _process.StandardOutput.BaseStream,
          (msg, _) =>
          {
            try
            {
              switch (msg)
              {
                case VariablesResponse variablesRes:
                  logger.LogInformation(
                    "[DBG] variables response: {message}",
                    JsonSerializer.Serialize(variablesRes, LoggingSerializerOptions));
                  if (applyValueConverters)
                  {
                    valueConverterService.RegisterVariablesReferences(variablesRes);
                  }
                  return Task.FromResult((ProtocolMessage?)variablesRes);

                case Response res:
                  logger.LogInformation(
                    "[DBG] response: {message}",
                    JsonSerializer.Serialize(res, LoggingSerializerOptions));
                  return Task.FromResult((ProtocolMessage?)res);

                case Event e:
                  if (e.EventName == "stopped")
                  {
                    valueConverterService.ClearVariablesReferenceMap();
                  }
                  logger.LogInformation(
                    "[DBG] event: {message}",
                    JsonSerializer.Serialize(e, LoggingSerializerOptions));
                  return Task.FromResult((ProtocolMessage?)e);

                default:
                  throw new Exception($"Unsupported DAP message from debugger: {msg}");
              }
            }
            catch (Exception e)
            {
              logger.LogError(e, "Exception in debugger DAP handler");
              throw;
            }
          });

        _proxy = new DebuggerProxy(clientDap, debuggerDap, debuggerProxyLogger);
        _proxy.Start(_cancellationTokenSource.Token, async () => await TriggerCleanup());

        await _proxy.Completion;

        _completionSource.SetResult(true);
      }
      catch (OperationCanceledException)
      {
        logger.LogInformation("Session task was canceled.");
        _completionSource.SetCanceled();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Unhandled exception in debug session.");
        _completionSource.SetException(ex);
        throw;
      }
      finally
      {
        logger.LogInformation("Session task finally block executing.");
      }
    }, _cancellationTokenSource.Token);

    return assignedPort;
  }

  private async Task TriggerCleanup()
  {
    if (Interlocked.CompareExchange(ref _cleanupTriggered, 1, 0) == 0)
    {
      logger.LogInformation("Cleanup triggered - beginning disposal sequence.");
    }

    await DisposeAsync();
  }

  private async Task ExecuteSessionCleanupAsync()
  {
    try
    {
      logger.LogInformation("Executing session cleanup. Invoking onDispose callback.");
      if (_onDispose != null)
      {
        await _onDispose();
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Exception in onDispose callback");
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.CompareExchange(ref _isDisposing, 1, 0) == 1)
    {
      logger.LogDebug("Disposal already in progress, skipping duplicate call.");
      return;
    }

    _disposalStartedSource.TrySetResult(true);

    logger.LogInformation("Beginning graceful shutdown of debug service.");

    if (_cancellationTokenSource?.IsCancellationRequested == false)
    {
      _cancellationTokenSource.Cancel();
    }

    if (_sessionTask != null)
    {
      try
      {
        await _sessionTask.WaitAsync(TimeSpan.FromSeconds(10));
        logger.LogDebug("Session task completed gracefully.");
      }
      catch (TimeoutException)
      {
        logger.LogDebug("Session task timed out after 10s, forcing cleanup.");
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Exception while waiting for session task.");
      }
    }

    CleanupResources();

    await ExecuteSessionCleanupAsync();

    logger.LogInformation("Debug service shutdown complete.");
  }

  public async ValueTask ForceDisposeAsync()
  {
    if (Interlocked.CompareExchange(ref _isDisposing, 1, 0) == 1)
    {
      logger.LogDebug("Already disposing, waiting for completion.");
      try
      {
        await Task.WhenAny(
          Task.Run(async () => await ExecuteSessionCleanupAsync()),
          Task.Delay(TimeSpan.FromSeconds(2))
        );
      }
      catch { }
      return;
    }

    _disposalStartedSource.TrySetResult(true);
    logger.LogInformation("Force disposal requested - immediately terminating debug service.");

    _cancellationTokenSource?.Cancel();

    CleanupResources();

    await ExecuteSessionCleanupAsync();

    logger.LogInformation("Force disposal complete.");
  }

  private void CleanupResources()
  {
    try
    {
      _client?.Close();
      logger.LogDebug("TCP client closed.");
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Error closing TCP client.");
    }

    try
    {
      _listener?.Stop();
      logger.LogDebug("TCP listener stopped.");
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Error stopping TCP listener.");
    }

    SafeDisposeProcess(_process, "Debugger");

    try
    {
      _cancellationTokenSource?.Dispose();
      logger.LogDebug("CancellationTokenSource disposed.");
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Error disposing CancellationTokenSource.");
    }
  }

  private void SafeDisposeProcess(Process? process, string processName)
  {
    if (process == null) return;

    try
    {
      if (!process.HasExited)
      {
        process.Kill();
        logger.LogDebug("{processName} process terminated.", processName);
      }
    }
    catch (InvalidOperationException)
    {
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to kill {processName} process.", processName);
    }
    finally
    {
      try
      {
        process.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to dispose {processName} process.", processName);
      }
    }
  }
}