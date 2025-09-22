using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Services;

public partial class NetcoreDbgService(ILogger<NetcoreDbgService> logger, ILogger<DebuggerProxy> debuggerProxyLogger) : INetcoreDbgService
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
  private DebuggerProxy? _debuggerProxy;
  private (System.Diagnostics.Process, int)? _vsTestAttach;
  private readonly Dictionary<int, InterceptableVariablesResponse> _pendingVariablesRequests = new();

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
        var clientDap = new Client(tcpStream, tcpStream, async (msg) =>
        {
          try
          {
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

        _process.Start();

        var debuggerDap = new Dap.Debugger(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream, async (msg) =>
        {
          try
          {
            switch (msg)
            {
              case InterceptableVariablesResponse varsRes:


                var pattern = @"System\.Collections\.Generic\.List(?:<.*?>|\\u003C.*?\\u003E)";
                var x = varsRes.Body.Variables.Find(x => x.VariablesReference != 0 && Regex.Match(x.Type, pattern).Success);

                var seq = varsRes.Seq + 1;

                  logger.LogInformation("[TCP] Expanding variables: {x}", JsonSerializer.Serialize(x, LoggingSerializerOptions));
                if (x is not null)
                {
                  var req = new InternalVariablesRequest { Seq = seq, Command = "variables", Type = "request", Arguments = new InternalVariablesArguments { VariablesReference = x.VariablesReference } };
                  var res = await _debuggerProxy!.RunInternalDebuggerRequestAsync(JsonSerializer.Serialize(req), seq, CancellationToken.None);
                  //next sequence

                  logger.LogInformation("[TCP] Expanding variables response: {x}", JsonSerializer.Serialize(res, LoggingSerializerOptions));
                }

                logger.LogInformation("[TCP] Intercepted variables response: {x}", JsonSerializer.Serialize(varsRes, LoggingSerializerOptions));
                // var res = await _debuggerProxy!.RunInternalDebuggerRequestAsync(JsonSerializer.Serialize(varsReq, SerializerOptions), varsReq.Seq, CancellationToken.None);
                // logger.LogInformation("[TCP] Intercepted variables response: {x}", JsonSerializer.Serialize(res, LoggingSerializerOptions));
                //TODO: send the var request to debugger without sending result back to client, 
                //Parse result and either send back request raw or send more requests to "unwrap" the type
                return JsonSerializer.Serialize(varsRes, SerializerOptions);
              case Response res:
                logger.LogInformation("[DBG] response: {message}", JsonSerializer.Serialize(res, LoggingSerializerOptions));
                return JsonSerializer.Serialize(res, SerializerOptions);
              case Event e:
                logger.LogInformation("[DBG] event: {message}", JsonSerializer.Serialize(e, LoggingSerializerOptions));
                return JsonSerializer.Serialize(e, SerializerOptions);
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

        _debuggerProxy = new DebuggerProxy(clientDap, debuggerDap, debuggerProxyLogger);

        _debuggerProxy.Start(_cancellationTokenSource.Token, TriggerCleanup);

        await _debuggerProxy.Completion;

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