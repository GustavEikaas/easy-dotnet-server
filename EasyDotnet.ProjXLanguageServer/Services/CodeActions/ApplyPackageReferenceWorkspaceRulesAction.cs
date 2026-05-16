using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services.CodeActions;

internal static class ApplyPackageReferenceWorkspaceRulesAction
{
  public static async Task<CodeAction?> BuildAsync(
      CsprojDocument doc,
      int rangeStart,
      int rangeEnd,
      IPackageReferenceEditPlanner planner,
      CancellationToken cancellationToken)
  {
    if (!doc.Uri.IsFile)
    {
      return null;
    }

    var element = Utils.AstSearch.FindElementOverlapping(doc.Root, rangeStart, rangeEnd, "PackageReference");
    if (element is null)
    {
      return null;
    }

    var packageId = GetAttributeValue(element, "Include");
    var version = GetAttributeValue(element, "Version");
    if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
    {
      return null;
    }

    var edit = await planner.PlanAddPackageReferenceAsync(
        new AddPackageReferenceEditRequest(doc.Uri.LocalPath, packageId, version),
        cancellationToken);

    return new CodeAction
    {
      Title = "Apply PackageReference using workspace rules",
      Kind = CodeActionKind.RefactorRewrite,
      Edit = edit,
    };
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