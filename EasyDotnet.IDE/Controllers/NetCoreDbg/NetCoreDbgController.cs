using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.NetcoreDbg;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.NetCoreDbg;

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