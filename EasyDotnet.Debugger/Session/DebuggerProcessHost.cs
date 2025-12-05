using System.Diagnostics;
using EasyDotnet.Debugger.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class DebuggerProcessHost(ILogger<DebuggerProcessHost> logger) : IDebuggerProcessHost
{
  private Process? _process;

  public Stream StandardInput => _process?.StandardInput.BaseStream
    ?? throw new InvalidOperationException("Process not started");

  public Stream StandardOutput => _process?.StandardOutput.BaseStream
    ?? throw new InvalidOperationException("Process not started");

  public event EventHandler? Exited;

  public void Start(string binaryPath, string arguments)
  {
    _process = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = binaryPath,
        Arguments = arguments,
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
      logger.LogDebug("Debugger process exited (PID: {Pid})", _process?.Id ?? -1);
      Exited?.Invoke(sender, args);
    };

    _process.Start();
    logger.LogInformation("Debugger process started (PID: {Pid})", _process.Id);
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