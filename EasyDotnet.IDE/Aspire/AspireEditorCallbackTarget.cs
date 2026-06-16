using EasyDotnet.Aspire.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Aspire;

/// <summary>
/// Local RPC target the spawned Aspire host calls back into. Fulfils run sessions
/// by relaying to the IDE's project-run machinery via <see cref="AspireRunService"/>.
/// </summary>
internal sealed class AspireEditorCallbackTarget(AspireRunService runService, JsonRpc rpc)
{
  [JsonRpcMethod(AspireRpcMethods.RunManagedResource, UseSingleObjectParameterDeserialization = true)]
  public async Task<RunManagedResourceResponse> RunManagedResourceAsync(RunManagedResourceRequest request, CancellationToken ct)
  {
    // When the editor captures the pid, report it back so the host can emit processRestarted.
    Task ReportPid(int pid) =>
        rpc.NotifyWithParameterObjectAsync(AspireRpcMethods.ReportProcessId, new ReportProcessIdRequest(request.RunId, pid));

    return new(await runService.RunAsync(request, ReportPid, ct));
  }

  [JsonRpcMethod(AspireRpcMethods.StopManagedResource, UseSingleObjectParameterDeserialization = true)]
  public Task StopManagedResourceAsync(StopManagedResourceRequest request)
  {
    runService.Stop(request.RunId);
    return Task.CompletedTask;
  }
}