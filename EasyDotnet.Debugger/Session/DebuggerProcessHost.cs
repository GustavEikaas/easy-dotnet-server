using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using EasyDotnet.Debugger.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class DebuggerProcessHost(ILogger<DebuggerProcessHost> logger) : IDebuggerProcessHost
{
  private Process? _process;
  private int _tcpPort;
  public int? ProcessId => _process?.Id;

  public event EventHandler? Exited;

  public void Start(string binaryPath)
  {
    _tcpPort = FindFreePort();

    _process = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = binaryPath,
        Arguments = $"--interpreter=vscode --server={_tcpPort}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      },
      EnableRaisingEvents = true,
    };

    _process.Exited += (sender, args) =>
    {
      logger.LogDebug("Debugger process exited (PID: {Pid})", _process?.Id ?? -1);
      Exited?.Invoke(sender, args);
    };

    _process.OutputDataReceived += (_, e) =>
    {
      if (!string.IsNullOrWhiteSpace(e.Data))
      {
        logger.LogInformation("ncdb: {Line}", e.Data);
      }
    };

    _process.ErrorDataReceived += (_, e)=>
    {
      if (!string.IsNullOrWhiteSpace(e.Data))
      {
        logger.LogInformation("ncdb: {Line}", e.Data);
      }
    };

    _process.Start();
    _process.BeginOutputReadLine();
    _process.BeginErrorReadLine();
    logger.LogInformation("Debugger process started (PID: {Pid}) on TCP port {Port}", _process.Id, _tcpPort);
  }

  public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
  {
    if (_process == null)
      throw new InvalidOperationException("Process not started");

    logger.LogInformation("Connecting to netcoredbg on port {Port}", _tcpPort);

    var deadline = DateTime.UtcNow + timeout;

    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _tcpPort, cancellationToken);
        logger.LogInformation("Connected to netcoredbg on port {Port}", _tcpPort);
        return client.GetStream();
      }
      catch (SocketException)
      {
        if (DateTime.UtcNow >= deadline)
          throw new TimeoutException($"Could not connect to netcoredbg on port {_tcpPort} within {timeout.TotalSeconds}s");

        await Task.Delay(50, cancellationToken);
      }
    }
  }

  private static int FindFreePort()
  {
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
  }

  public async Task WaitForExitAsync(CancellationToken cancellationToken)
  {
    if (_process == null)
    {
      logger.LogDebug("WaitForExitAsync called but process is null");
      return;
    }

    await _process.WaitForExitAsync(cancellationToken);
  }

  public void Kill()
  {
    if (_process == null)
    {
      logger.LogDebug("Kill called but process is null");
      return;
    }

    try
    {
      if (_process.HasExited)
      {
        logger.LogDebug("Process already exited (PID: {Pid})", _process.Id);
        return;
      }

      var pid = _process.Id;
      logger.LogInformation("Killing debugger process (PID: {Pid})", pid);

      _process.Kill(entireProcessTree: true); // ← IMPORTANT: Kill child processes too! 

      // Wait a bit to confirm it's dead
      if (!_process.WaitForExit(2000))
      {
        logger.LogWarning("Process did not exit within 2 seconds (PID: {Pid})", pid);
      }
      else
      {
        logger.LogInformation("Process killed successfully (PID: {Pid})", pid);
      }
    }
    catch (InvalidOperationException)
    {
      logger.LogDebug("Process already exited");
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Error killing process (PID: {Pid})", _process?.Id ?? -1);
    }
  }

  public async ValueTask DisposeAsync()
  {
    logger.LogDebug("DisposeAsync called");

    Kill();

    if (_process != null)
    {
      try
      {
        _process.Dispose();
        logger.LogDebug("Process disposed");
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Error disposing process");
      }
    }

    await Task.CompletedTask;
  }
}