using EasyDotnet.Controllers;
using EasyDotnet.IDE.Workspace.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Workspace.Controllers;

public class WorkspaceRunController(WorkspaceService service) : BaseController
{
  [JsonRpcMethod("workspace/run", UseSingleObjectParameterDeserialization = true)]
  public async Task RunAsync(WorkspaceRunRequest request, CancellationToken ct) => await service.RunAsync(request, ct);
}