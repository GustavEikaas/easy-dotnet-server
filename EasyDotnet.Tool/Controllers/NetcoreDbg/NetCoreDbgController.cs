using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Services;
using EasyDotnet.Services.NetCoreDbg;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.NetcoreDbg;

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName
);

public sealed record DebuggerStartResponse(bool Success);

public class NetCoreDbgController(MsBuildService msBuildService, LaunchProfileService launchProfileService, NetcoreDbgService netcoreDbgService) : BaseController
{
  [JsonRpcMethod("debugger/start")]
  public async Task<DebuggerStartResponse> StartDebugger(DebuggerStartRequest request, CancellationToken cancellationToken)
  {
    var project = await msBuildService.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.Configuration ?? "Debug", cancellationToken);
    var launchProfile =
        !string.IsNullOrEmpty(request.LaunchProfileName)
            ? launchProfileService
                .GetLaunchProfiles(Path.GetDirectoryName(request.TargetPath)!)
                .TryGetValue(request.LaunchProfileName, out var profile)
                    ? profile
                    : null
            : null;

    netcoreDbgService.Start(project, request.TargetPath, launchProfile);
    return new DebuggerStartResponse(true);
  }
}