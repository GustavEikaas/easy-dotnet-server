using EasyDotnet.Aspire.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Aspire;

internal sealed class AspireEditorCallbackTarget(AspireRunService runService, AspireDebugService debugService, JsonRpc rpc)
{
  [JsonRpcMethod(AspireRpcMethods.RunManagedResource, UseSingleObjectParameterDeserialization = true)]
  public async Task<RunManagedResourceResponse> RunManagedResourceAsync(RunManagedResourceRequest request, CancellationToken ct)
  {
    Task ReportPid(int pid) =>
        rpc.NotifyWithParameterObjectAsync(AspireRpcMethods.ReportProcessId, new ReportProcessIdRequest(request.RunId, pid));

    var exitCode = request.Debug ? await debugService.DebugAsync(request, ReportPid, ct) : await runService.RunAsync(request, ReportPid, ct);

    return new(exitCode);
  }

  [JsonRpcMethod(AspireRpcMethods.StopManagedResource, UseSingleObjectParameterDeserialization = true)]
  public async Task StopManagedResourceAsync(StopManagedResourceRequest request)
  {
    if (debugService.Owns(request.RunId))
    {
      await debugService.StopAsync(request.RunId);
    }
    else
    {
      runService.Stop(request.RunId);
    }
  }
}