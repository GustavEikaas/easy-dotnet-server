using EasyDotnet.Controllers;
using EasyDotnet.IDE.Workspace.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Workspace.Controllers;

public class WorkspaceController(WorkspaceService service) : BaseController
{
  [JsonRpcMethod("workspace/run", UseSingleObjectParameterDeserialization = true)]
  public async Task RunAsync(WorkspaceRunRequest request, CancellationToken ct) =>
      await service.RunAsync(request, ct);

  [JsonRpcMethod("workspace/debug", UseSingleObjectParameterDeserialization = true)]
  public async Task DebugAsync(WorkspaceDebugRequest request, CancellationToken ct) =>
      await service.DebugAsync(request, ct);
}