using EasyDotnet.Controllers;
using EasyDotnet.IDE.ProjectReference.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.ProjectReference.Controllers;

public class ProjectReferenceController(ProjectReferenceService service) : BaseController
{
  [JsonRpcMethod("project/add-reference-interactive", UseSingleObjectParameterDeserialization = true)]
  public async Task AddAsync(ProjectReferenceRequest request, CancellationToken ct) =>
      await service.AddProjectReferenceInteractiveAsync(request.ProjectPath, ct);

  [JsonRpcMethod("project/remove-reference-interactive", UseSingleObjectParameterDeserialization = true)]
  public async Task RemoveAsync(ProjectReferenceRequest request, CancellationToken ct) =>
      await service.RemoveProjectReferenceInteractiveAsync(request.ProjectPath, ct);
}