using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.NetCoreDbg;

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName
);

public sealed record DebuggerStartResponse(bool Success, int Port);

public class NetCoreDbgController(IDebugOrchestrator debugOrchestrator) : BaseController
{
  [JsonRpcMethod("debugger/start")]
  public async Task<DebuggerStartResponse> StartDebugger(DebuggerStartRequest request, CancellationToken cancellationToken)
  {
    var port = await debugOrchestrator.StartClientDebugSessionAsync(
    request.TargetPath,
    request,
    cancellationToken);

    return new DebuggerStartResponse(true, port);
  }

}