using EasyDotnet.Controllers;
using EasyDotnet.IDE.Solution.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Solution.Controllers;

public class SolutionManagementController(SolutionManagementService service) : BaseController
{
  [JsonRpcMethod("solution/add-project")]
  public async Task AddProjectAsync(CancellationToken ct) =>
      await service.AddProjectInteractiveAsync(ct);

  [JsonRpcMethod("solution/remove-project")]
  public async Task RemoveProjectAsync(CancellationToken ct) =>
      await service.RemoveProjectInteractiveAsync(ct);
}