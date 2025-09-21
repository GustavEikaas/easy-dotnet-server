using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Infrastructure.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.NetCoreDbg;

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName
);

public sealed record DebuggerStartResponse(bool Success);

public class NetCoreDbgController(IMsBuildService msBuildService, ILaunchProfileService launchProfileService, NetcoreDbgService netcoreDbgService) : BaseController
{
  [JsonRpcMethod("debugger/start")]
  public async Task<DebuggerStartResponse> StartDebugger(DebuggerStartRequest request, CancellationToken cancellationToken)
  {
    var project = await msBuildService.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.Configuration ?? "Debug", cancellationToken);
    var launchProfile = !string.IsNullOrEmpty(request.LaunchProfileName)
        ? (launchProfileService.GetLaunchProfiles(Path.GetDirectoryName(request.TargetPath)!)
           is { } profiles && profiles.TryGetValue(request.LaunchProfileName, out var profile)
              ? profile
              : null)
        : null;

    netcoreDbgService.Start(project, request.TargetPath, launchProfile);
    return new DebuggerStartResponse(true);
  }
}