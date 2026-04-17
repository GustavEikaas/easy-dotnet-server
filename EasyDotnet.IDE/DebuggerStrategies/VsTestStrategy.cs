using System.Diagnostics;
using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.IDE.Dap;
using EasyDotnet.IDE.Types;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class VsTestStrategy(string targetPath, string? projectDir, ILogger<VsTestStrategy> logger) : IDebugSessionStrategy
{
  private Process? _vsTestWrapperProcess;
  private int _testHostPid;
  private readonly TaskCompletionSource<int> _processIdTcs = new();

  public Task PrepareAsync(CancellationToken ct)
  {
    logger.LogInformation("Starting VsTest host for {TargetPath}", targetPath);

    var (process, pid) = VsTestHelper.StartTestProcess(targetPath);

    _vsTestWrapperProcess = process;
    _testHostPid = pid;

    if (_testHostPid > 0)
    {
      logger.LogInformation("VsTest host process started with PID: {pid}", _testHostPid);
      _processIdTcs.TrySetResult(_testHostPid);
    }
    else
    {
      logger.LogError("VsTest host process started but PID is invalid: {pid}", _testHostPid);
      _processIdTcs.TrySetException(new InvalidOperationException("Failed to start VsTest host process"));
    }

    return Task.CompletedTask;
  }

  public Task TransformRequestAsync(InterceptableAttachRequest request, IDebuggerProxy proxy)
  {
    if (_vsTestWrapperProcess is null)
      throw new InvalidOperationException("Strategy has not been prepared.");

    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.ProcessId = _testHostPid;
    request.Arguments.Cwd = projectDir;

    return Task.CompletedTask;
  }

  public void OnDebugSessionReady(DebugSession debugSession, IDebuggerProxy proxy)
  {

  }

  public Task<int>? GetProcessIdAsync() => _processIdTcs.Task;

  public ValueTask DisposeAsync()
  {
    if (_vsTestWrapperProcess != null)
    {
      SafeDisposeProcess(_vsTestWrapperProcess, "VsTest Wrapper");
    }

    if (_testHostPid > 0)
    {
      SafeDisposeProcessById(_testHostPid, "VsTest Host");
    }

    return ValueTask.CompletedTask;
  }


  private void SafeDisposeProcessById(int pid, string processName)
  {
    try
    {
      var process = Process.GetProcessById(pid);
      SafeDisposeProcess(process, $"{processName} (PID: {pid})");
    }
    catch (ArgumentException)
    {
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to clean up {ProcessName} with PID {Pid}", processName, pid);
    }
  }

  private void SafeDisposeProcess(Process process, string processName)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill();
        logger.LogInformation("Killed {ProcessName}", processName);
      }
    }
    catch (InvalidOperationException)
    {
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to kill {ProcessName}", processName);
    }
    finally
    {
      try { process.Dispose(); } catch { }
    }
  }
}