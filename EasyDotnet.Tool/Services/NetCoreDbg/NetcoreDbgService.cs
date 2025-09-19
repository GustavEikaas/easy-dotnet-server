using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Controllers.LaunchProfile;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services.NetCoreDbg;

public class NetcoreDbgService(ILogger<NetcoreDbgService> logger)
{
  private readonly TaskCompletionSource<bool> _completionSource = new();
  private CancellationTokenSource? _cancellationTokenSource;
  private Task? _sessionTask;
  private TcpListener? _listener;
  private Process? _process;

  private DotnetProjectProperties? _project;
  private LaunchProfile? _launchProfile;
  private string? _projectPath;

  public Task Completion => _completionSource.Task;

  public void Start(DotnetProjectProperties project, string projectPath, LaunchProfile? launchProfile)
  {
    _project = project;
    _projectPath = projectPath;
    _launchProfile = launchProfile;
    _cancellationTokenSource = new CancellationTokenSource();

    _sessionTask = Task.Run(async () =>
            {
              try
              {
                logger.LogInformation("Waiting for client...");
                _listener = new TcpListener(IPAddress.Any, 8086);
                _listener.Start();
                logger.LogInformation("Listening for client on port 8086.");

                var client = await _listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
                logger.LogInformation("Client connected.");

                var tcpStream = client.GetStream();
                var clientDap = new Client(tcpStream, tcpStream, x =>
            {
              logger.LogInformation("[TCP] message: {message}", x);
              return x;
            });

                _process = new Process
                {
                  StartInfo = new ProcessStartInfo
                  {
                    FileName = "netcoredbg",
                    Arguments = "--interpreter=vscode",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                  },
                  EnableRaisingEvents = true,
                };
                _process.Start();

                var debuggerDap = new Infrastructure.Dap.Debugger(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream, y =>
            {
              logger.LogInformation("[DBG] message: {message}", y);
              return y;
            });

                var proxy = new DebuggerProxy(clientDap, debuggerDap);
                proxy.Start(_cancellationTokenSource.Token);

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

  public async ValueTask DisposeAsync()
  {
    _cancellationTokenSource?.Cancel();

    if (_sessionTask != null)
    {
      try
      {
        await _sessionTask.WaitAsync(TimeSpan.FromSeconds(5));
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

    if (_listener != null)
    {
      _listener.Stop();
      logger.LogInformation("TCP listener stopped.");
    }

    if (_process != null && !_process.HasExited)
    {
      try
      {
        _process.Kill();
        logger.LogInformation("Debugger process killed.");
      }
      catch (InvalidOperationException ex)
      {
        logger.LogWarning(ex, "Could not kill process, it may have already exited.");
      }
    }
    _cancellationTokenSource?.Dispose();
  }
}