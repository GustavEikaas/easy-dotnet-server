using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Infrastructure.Dap;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.NetCoreDbg;

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName
);

public sealed record DebuggerStartResponse(bool Success, int Port);

public class NetCoreDbgController(
  IMsBuildService msBuildService,
  ILaunchProfileService launchProfileService,
  INetcoreDbgService netcoreDbgService,
  IClientService clientService,
  ILogger<NetCoreDbgController> logger) : BaseController
{
  [JsonRpcMethod("debugger/start")]
  public async Task<DebuggerStartResponse> StartDebugger(DebuggerStartRequest request, CancellationToken cancellationToken)
  {
    var project = await msBuildService.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.Configuration ?? "Debug", cancellationToken);
    var launchProfile = !string.IsNullOrEmpty(request.LaunchProfileName)
        ? (launchProfileService.GetLaunchProfiles(request.TargetPath)
           is { } profiles && profiles.TryGetValue(request.LaunchProfileName, out var profile)
              ? profile
              : null)
        : null;

    var binaryPath = clientService.ClientOptions?.DebuggerOptions?.BinaryPath;
    if (string.IsNullOrEmpty(binaryPath))
    {
      throw new Exception("Failed to start debugger, no binary path provided");
    }

    var res = StartVsTestIfApplicable(project, request.TargetPath);

    var port = await netcoreDbgService.Start(binaryPath, async (request) => await InitializeRequestRewriter.CreateInitRequestBasedOnProjectType(project, launchProfile, request, project.ProjectDir!, res?.Item2), clientService?.ClientOptions?.DebuggerOptions?.ApplyValueConverters ?? false, () =>
    {
      if (res is { } value)
      {
        var (process, pid) = value;
        SafeDisposeProcess(res.Value.Item1, "VsTest");
        SafeDisposeProcessById(pid, "VsTestHost");
      }
    });
    // TODO: register debug session
    return new DebuggerStartResponse(true, port);
  }

  private static (Process, int)? StartVsTestIfApplicable(DotnetProject project, string projectPath) => project.IsTestProject && !project.TestingPlatformDotnetTestSupport ? VsTestHelper.StartTestProcess(projectPath) : null;

  private void SafeDisposeProcessById(int pid, string processName)
  {
    try
    {
      var process = Process.GetProcessById(pid);
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

  private void SafeDisposeProcess(Process? process, string processName)
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
}