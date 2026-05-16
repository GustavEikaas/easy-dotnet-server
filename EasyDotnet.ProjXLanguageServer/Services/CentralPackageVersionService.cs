using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;

namespace EasyDotnet.ProjXLanguageServer.Services;

public sealed record CentralPackageVersionInfo(string PackageId, string? Version);

public interface ICentralPackageVersionService
{
  Task<IReadOnlyList<CentralPackageVersionInfo>> GetPackageVersionsAsync(string projectPath, CancellationToken cancellationToken);
}

public sealed class CentralPackageVersionService(
    IProjXWorkspaceHierarchyService hierarchyService,
    IProjXDocumentTextProvider textProvider) : ICentralPackageVersionService
{
  public async Task<IReadOnlyList<CentralPackageVersionInfo>> GetPackageVersionsAsync(
      string projectPath,
      CancellationToken cancellationToken)
  {
    var hierarchy = await hierarchyService.ResolveAsync(projectPath, cancellationToken);
    if (!hierarchy.ManagePackageVersionsCentrally
        || string.IsNullOrWhiteSpace(hierarchy.DirectoryPackagesPropsPath)
        || !textProvider.TryGetText(hierarchy.DirectoryPackagesPropsPath, out var text))
    {
      return [];
    }

    var doc = new CsprojDocument(new Uri(Path.GetFullPath(hierarchy.DirectoryPackagesPropsPath)), text, version: 0);
    var packages = new Dictionary<string, CentralPackageVersionInfo>(StringComparer.OrdinalIgnoreCase);
    foreach (var element in AstSearch.Elements(doc.Root))
    {
      if (!string.Equals(element.Name, "PackageVersion", StringComparison.Ordinal))
      {
        continue;
      }

      var packageId = GetAttributeValue(element, "Include") ?? GetAttributeValue(element, "Update");
      if (string.IsNullOrWhiteSpace(packageId))
      {
        continue;
      }

      packages[packageId] = new CentralPackageVersionInfo(packageId, GetAttributeValue(element, "Version"));
    }

    return [.. packages.Values.OrderBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)];
  }

  private static string? GetAttributeValue(IXmlElementSyntax element, string name)
  {
    foreach (var attr in element.Attributes)
    {
      if (string.Equals(attr.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        return attr.Value;
      }
    }

    return null;
  }
}
