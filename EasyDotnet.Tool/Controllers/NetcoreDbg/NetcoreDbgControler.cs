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

public class NetcoreDbgControler(MsBuildService msBuildService, NetcoreDbgService netcoreDbgService) : BaseController
{
  [JsonRpcMethod("debugger/start")]
  public async Task<DebuggerStartResponse> StartDebugger(DebuggerStartRequest request, CancellationToken cancellationToken)
  {
    var x = await msBuildService.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.Configuration ?? "Debug", cancellationToken);
    //TODO: launch profile env
    netcoreDbgService.Start(x, request.TargetPath, launchProfileName: null);
    return new DebuggerStartResponse(true);
  }
}