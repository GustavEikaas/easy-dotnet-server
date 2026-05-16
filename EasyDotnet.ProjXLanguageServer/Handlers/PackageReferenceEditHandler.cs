using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public sealed class PackageReferenceEditHandler(
    IPackageReferenceEditPlanner packageReferenceEditPlanner) : BaseController
{
  [JsonRpcMethod("projx/package/add-reference-edit", UseSingleObjectParameterDeserialization = true)]
  public Task<WorkspaceEdit> AddPackageReferenceEditAsync(
      AddPackageReferenceEditRequest request,
      CancellationToken cancellationToken)
  {
    return packageReferenceEditPlanner.PlanAddPackageReferenceAsync(request, cancellationToken);
  }
}
