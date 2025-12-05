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
      logger.LogDebug("Debugger process exited");
      Exited?.Invoke(sender, args);
    };

    _process.Start();
    logger.LogInformation("Debugger process started (PID: {pid})", _process.Id);
  }

  public async Task WaitForExitAsync(CancellationToken cancellationToken)
  {
    if (_process == null) return;
    await _process.WaitForExitAsync(cancellationToken);
  }

  public void Kill()
  {
    if (_process == null) return;

    try
    {
      if (!_process.HasExited)
      {
        _process.Kill();
        logger.LogDebug("Debugger process killed");
      }
    }
    catch (InvalidOperationException)
    {
      logger.LogDebug("Process already exited");
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Error killing process");
    }
  }

  public async ValueTask DisposeAsync()
  {
    Kill();
    _process?.Dispose();
    await Task.CompletedTask;
  }
}
