using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.IDE.Types;
using EasyDotnet.Infrastructure.Dap;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class VsTestStrategy(ILogger<VsTestStrategy> logger) : IDebugSessionStrategy
{
  private Process? _vsTestWrapperProcess;
  private int _testHostPid;
  private DotnetProject? _project;

  public Task PrepareAsync(DotnetProject project, CancellationToken ct)
  {
    _project = project;


    logger.LogInformation("Starting VsTest host for {Project}", project.ProjectName);

    var (process, pid) = VsTestHelper.StartTestProcess(project.TargetPath!);

    _vsTestWrapperProcess = process;
    _testHostPid = pid;

    return Task.CompletedTask;
  }

  public Task TransformRequestAsync(InterceptableAttachRequest request)
  {
    if (_vsTestWrapperProcess is null || _project is null)
    {
      throw new InvalidOperationException("Strategy has not been prepared.");
    }

    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.ProcessId = _testHostPid;

    request.Arguments.Cwd = _project.ProjectDir;

    return Task.CompletedTask;
  }

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