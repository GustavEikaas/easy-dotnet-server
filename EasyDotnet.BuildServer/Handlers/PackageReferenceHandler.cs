using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Evaluation;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class PackageReferenceHandler
{
  [JsonRpcMethod("projects/list-package-references", UseSingleObjectParameterDeserialization = true)]
  public InstalledPackageReference[] ListPackageReferences(ListPackageReferencesRequest request)
  {
    if (!File.Exists(request.ProjectPath))
      throw new FileNotFoundException("Project file not found", request.ProjectPath);

    using var projectCollection = new ProjectCollection();
    var project = projectCollection.LoadProject(request.ProjectPath);

    return [.. project
        .GetItems("PackageReference")
        .Select(item => new InstalledPackageReference(
            item.EvaluatedInclude,
            item.GetMetadataValue("Version")))
        .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)];
  }
}