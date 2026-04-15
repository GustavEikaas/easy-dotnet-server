using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers;

/// <summary>
/// Exposes internal diagnostics about the running BuildServer.
/// Useful for verifying that global.json pinning is respected at spawn time.
/// </summary>
public class DiagnosticsController(IBuildHostManager buildHostManager) : BaseController
{
  [JsonRpcMethod("diagnostics/buildserver")]
  public Task<BuildServerDiagnosticsResponse> GetBuildServerDiagnosticsAsync(CancellationToken ct) =>
      buildHostManager.GetBuildServerDiagnosticsAsync(ct);
}