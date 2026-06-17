using EasyDotnet.Aspire.Contracts;
using EasyDotnet.Aspire.RunSessionManager;
using StreamJsonRpc;

namespace EasyDotnet.Aspire;

/// <summary>
/// <see cref="IIdeCallback"/> implemented over the named-pipe JSON-RPC connection
/// back to the IDE.
/// </summary>
public sealed class IdeCallback(JsonRpc rpc) : IIdeCallback
{
  public async Task<int> RunManagedResourceAsync(RunManagedResourceRequest request, CancellationToken ct)
  {
    var response = await rpc.InvokeWithParameterObjectAsync<RunManagedResourceResponse>(
        AspireRpcMethods.RunManagedResource, request, ct);
    return response.ExitCode;
  }

  public Task StopManagedResourceAsync(string runId, CancellationToken ct) =>
      rpc.InvokeWithParameterObjectAsync(
          AspireRpcMethods.StopManagedResource, new StopManagedResourceRequest(runId), ct);
}