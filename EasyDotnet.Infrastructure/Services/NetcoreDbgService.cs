using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.Infrastructure.Dap;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Services;

public class NetcoreDbgService(ILogger<NetcoreDbgService> logger, ILogger<DebuggerProxy> debuggerProxyLogger, INotificationService notificationService) : INetcoreDbgService
{
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  private static readonly JsonSerializerOptions LoggingSerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

  private readonly TaskCompletionSource<bool> _completionSource = new();
  private CancellationTokenSource? _cancellationTokenSource;
  private Task? _sessionTask;
  private TcpListener? _listener;
  private System.Diagnostics.Process? _process;
  private TcpClient? _client;
  private (System.Diagnostics.Process, int)? _vsTestAttach;
  private Task? _disposeTask;
  private int _clientSeq;
  private int GetNextSequence() => Interlocked.Increment(ref _clientSeq);

  public Task Completion => _completionSource.Task;

  public async Task<int> Start(string binaryPath, DotnetProject project, string projectPath, LaunchProfile? launchProfile, (System.Diagnostics.Process, int)? vsTestAttach)
  {
    if (_disposeTask != null)
    {
      logger.LogInformation("Waiting for previous debugger session to fully dispose...");
      try
      {
        await _disposeTask;
      }
      catch
      {
      }
    }
    _vsTestAttach = vsTestAttach;
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
          _client = await _listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
          logger.LogInformation("Client connected.");
        }
        catch (TimeoutException)
        {
          logger.LogWarning("No client connected within 30 seconds. Triggering cleanup.");
          TriggerCleanup();
          _completionSource.SetCanceled();
          return;
        }

        var tcpStream = _client.GetStream();
        var clientDap = new Client(tcpStream, tcpStream, async (msg) =>
        {
          try
          {
            msg.Seq = GetNextSequence();
            switch (msg)
            {
              case InterceptableAttachRequest attachReq:
                var modified = await InitializeRequestRewriter.CreateInitRequestBasedOnProjectType(
                    project,
                    launchProfile,
                    attachReq,
                    Path.GetDirectoryName(projectPath)!,
                    attachReq.Seq,
                    vsTestAttach?.Item2
                );
                logger.LogInformation("[TCP] Intercepted attach request: {modified}", JsonSerializer.Serialize(modified, LoggingSerializerOptions));
                return JsonSerializer.Serialize(modified, SerializerOptions);

              case SetBreakpointsRequest setBpReq:
                if (OperatingSystem.IsWindows())
                {
                  logger.LogInformation("[TCP] Intercepted set breakpoints request: Normalizing path separators");
                  setBpReq.Arguments.Source.Path =
                      setBpReq.Arguments.Source.Path.Replace('/', '\\');
                }

                logger.LogInformation(
                    "[TCP] setBreakpoints request: {message}",
                    JsonSerializer.Serialize(setBpReq, LoggingSerializerOptions));

                return JsonSerializer.Serialize(setBpReq, SerializerOptions);

              case Request req:
                logger.LogInformation("[TCP] request: {message}", JsonSerializer.Serialize(req, LoggingSerializerOptions));
                Console.WriteLine($"Request command: {req.Command}");
                return JsonSerializer.Serialize(req, SerializerOptions);

              default:
                throw new Exception($"Unsupported DAP message from client: {msg}");
            }
          }
          catch (Exception e)
          {
            logger.LogError("Exception {e}", e);
            throw;
          }
        });

        _process = new System.Diagnostics.Process
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

        _process.Exited += (sender, args) =>
        {
          logger.LogInformation("Debugger process exited.");
          TriggerCleanup();
        };

        try
        {
          _process.Start();
        }
        catch (Exception e)
        {
          await notificationService.DisplayError($"Debugger failed to start: {e.Message}");
        }

        var debuggerDap = new Dap.Debugger(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream, (msg) =>
        {
          try
          {
            switch (msg)
            {
              case Response res:
                logger.LogInformation("[DBG] response: {message}", JsonSerializer.Serialize(res, LoggingSerializerOptions));
                return Task.FromResult(JsonSerializer.Serialize(res, SerializerOptions));
              case Event e:
                logger.LogInformation("[DBG] event: {message}", JsonSerializer.Serialize(e, LoggingSerializerOptions));
                return Task.FromResult(JsonSerializer.Serialize(e, SerializerOptions));
              default:
                throw new Exception($"Unsupported DAP message from debugger: {msg}");
            }
          }
          catch (Exception e)
          {
            logger.LogError("Exception {e}", e);
            throw;
          }
        });

        var proxy = new DebuggerProxy(clientDap, debuggerDap, debuggerProxyLogger);

        proxy.Start(_cancellationTokenSource.Token, TriggerCleanup);

        await proxy.Completion;

        _completionSource.SetResult(true);
      }
      catch (OperationCanceledException)
      {
        logger.LogInformation("Operation was canceled.");
        _completionSource.SetCanceled();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "An unhandled exception occurred in the debugging session.");
        _completionSource.SetException(ex);
        throw;
      }
    }, _cancellationTokenSource.Token);

    return assignedPort;
  }

  private void TriggerCleanup()
  {
    logger.LogInformation("Triggering cleanup due to disconnection or process exit.");

    if (_cancellationTokenSource?.IsCancellationRequested != false)
    {
      return;
    }

    _ = Task.Run(async () =>
    {
      try
      {
        await DisposeAsync();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error during cleanup");
      }
    });
  }

  public async ValueTask DisposeAsync()
  {
    _clientSeq = 0;
    if (_cancellationTokenSource?.IsCancellationRequested == false)
    {
      _cancellationTokenSource.Cancel();
    }

    _disposeTask = Task.Run(async () =>
    {
      if (_sessionTask != null)
      {
        try
        {
          await _sessionTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
          logger.LogWarning("Graceful shutdown timed out. Forcing cleanup.");
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Exception during graceful shutdown.");
        }
      }

      _client?.Close();
      _listener?.Stop();
      SafeDisposeProcess(_process, "Debugger");

      if (_vsTestAttach.HasValue)
      {
        var (process, pid) = _vsTestAttach.Value;
        SafeDisposeProcess(process, "VsTest");
        SafeDisposeProcessById(pid, "VsTestHost");
      }

      _cancellationTokenSource?.Dispose();
    });

    await _disposeTask;
  }
  private void SafeDisposeProcess(System.Diagnostics.Process? process, string processName)
  {
    if (process == null) return;

    try
    {
      if (!process.HasExited)
      {
        process.Kill();
        logger.LogInformation("Killed {processName} process", processName);
      }
      else
      {
        logger.LogInformation("{processName} process already exited", processName);
      }
    }
    catch (InvalidOperationException)
    {
      logger.LogInformation("{processName} process already exited", processName);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to kill {processName} process", processName);
    }
    finally
    {
      try
      {
        process.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to dispose {processName} process", processName);
      }
    }
  }

  private void SafeDisposeProcessById(int pid, string processName)
  {
    try
    {
      var process = System.Diagnostics.Process.GetProcessById(pid);
      SafeDisposeProcess(process, $"{processName} (PID: {pid})");
    }
    catch (ArgumentException)
    {
      logger.LogInformation("{processName} (PID: {pid}) not found", processName, pid);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to get {processName} process by PID: {pid}", processName, pid);
    }
  }
}