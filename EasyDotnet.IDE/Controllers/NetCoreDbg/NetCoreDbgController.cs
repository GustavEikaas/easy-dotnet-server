using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Infrastructure.Dap;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.NetCoreDbg;

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName
);

public sealed record DebuggerStartResponse(bool Success, int Port);

public class NetCoreDbgController(IMsBuildService msBuildService, ILaunchProfileService launchProfileService, INetcoreDbgService netcoreDbgService, IClientService clientService) : BaseController
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

    var port = await netcoreDbgService.Start(binaryPath, project, request.TargetPath, launchProfile, res);
    await clientService.RequestStartDebugSession("127.0.0.1", port);
    return new DebuggerStartResponse(true, port);
  }

  private static (Process, int)? StartVsTestIfApplicable(DotnetProject project, string projectPath) => project.IsTestProject && !project.TestingPlatformDotnetTestSupport ? VsTestHelper.StartTestProcess(projectPath) : null;
}