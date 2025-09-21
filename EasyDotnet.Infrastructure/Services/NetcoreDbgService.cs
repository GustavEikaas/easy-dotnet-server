using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Services;

public partial class NetcoreDbgService(ILogger<NetcoreDbgService> logger, ILogger<DebuggerProxy> debuggerProxyLogger) : INetcoreDbgService
{
  private static readonly JsonSerializerOptions SeralizerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  private readonly TaskCompletionSource<bool> _completionSource = new();
  private CancellationTokenSource? _cancellationTokenSource;
  private Task? _sessionTask;
  private TcpListener? _listener;
  private System.Diagnostics.Process? _process;
  private TcpClient? _client;
  private (System.Diagnostics.Process, int)? _vsTestAttach;

  public Task Completion => _completionSource.Task;

  public void Start(string binaryPath, DotnetProject project, string projectPath, LaunchProfile? launchProfile, (System.Diagnostics.Process, int)? vsTestAttach)
  {
    _vsTestAttach = vsTestAttach;
    _cancellationTokenSource = new CancellationTokenSource();

    _sessionTask = Task.Run(async () =>
    {
      try
      {
        logger.LogInformation("Waiting for client...");
        _listener = new TcpListener(IPAddress.Any, 8086);
        _listener.Start();
        logger.LogInformation("Listening for client on port 8086.");

        try
        {
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
        var clientDap = new Client(tcpStream, tcpStream, async (x) =>
        {
          logger.LogInformation("[TCP] message: {message}", x);

          if (AttachRequestPattern().IsMatch(x))
          {
            try
            {
              var message = JsonSerializer.Deserialize<InterceptableAttachRequest>(x, SeralizerOptions);
              if (message?.Arguments?.Request == "attach" && message.Command?.Trim() == "attach")
              {
                var seq = message.Seq;

                var modified = await InitializeRequestRewriter.CreateInitRequestBasedOnProjectType(
                    project,
                    launchProfile,
                    message,
                    Path.GetDirectoryName(projectPath)!,
                    seq,
                    vsTestAttach?.Item2
                );

                logger.LogInformation("[TCP] Intercepted attach request: {modified}", modified);
                return modified;
              }
            }
            catch (JsonException ex)
            {
              logger.LogError(ex, "Failed to deserialize message (likely not attach): {message}", x);
            }
          }

          return x;
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

        _process.Start();

        var debuggerDap = new Dap.Debugger(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream, y =>
        {
          logger.LogInformation("[DBG] message: {message}", y);
          return Task.FromResult(y);
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
    _cancellationTokenSource?.Cancel();

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
        logger.LogError(ex, "An exception occurred during graceful shutdown.");
      }
    }

    if (_client != null)
    {
      try
      {
        _client.Close();
        _client.Dispose();
        logger.LogInformation("TCP client closed.");
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Error closing TCP client.");
      }
    }

    if (_listener != null)
    {
      _listener.Stop();
      logger.LogInformation("TCP listener stopped.");
    }

    SafeDisposeProcess(_process, "Debugger");

    if (_vsTestAttach.HasValue)
    {
      var (process, pid) = _vsTestAttach.Value;

      SafeDisposeProcess(process, "VsTest");
      SafeDisposeProcessById(pid, "VsTestHost");
    }
    _cancellationTokenSource?.Dispose();
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


  [GeneratedRegex(@"""select_project"":\s*""REWRITE_ATTACH""")]
  private static partial Regex AttachRequestPattern();
}

public record class InterceptableAttachRequest
{
  public InterceptableAttachRequestArguments Arguments { get; set; } = default!;
  public string Command { get; set; } = string.Empty;
  public int Seq { get; set; }
  public string Type { get; set; } = string.Empty;
}

public record class InterceptableAttachRequestArguments
{
  public string Request { get; set; } = string.Empty;
  public string Cwd { get; set; } = string.Empty;
  public int ProcessId { get; set; }
  public string Program { get; set; } = string.Empty;
  public string Type { get; set; } = string.Empty;
  public Dictionary<string, string>? Env { get; set; }
}