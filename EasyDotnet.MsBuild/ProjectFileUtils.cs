using Microsoft.Language.Xml;

namespace EasyDotnet.MsBuild;

public static class ProjectFileUtils
{
  /// <summary>
  /// Adds a PackageReference to a .csproj file.
  /// Finds the first ItemGroup without a Condition that contains at least one PackageReference.
  /// If none exists, creates a new ItemGroup.
  /// </summary>
  public static void AddPackageReference(string csprojPath, string packageName, string? version = null)
  {
    var root = OpenCsprojFile(csprojPath);

    var targetItemGroup = root
        .Descendants()
        .OfType<XmlElementSyntax>()
        .Where(e => e.IsItemGroup() && e.GetAttribute("Condition") == null)
        .FirstOrDefault(e => e.Content
            .OfType<XmlEmptyElementSyntax>()
            .Any(c => c.IsPackageReference()));

    var packageRefXml = string.IsNullOrEmpty(version)
        ? $"<PackageReference Include=\"{packageName}\" />"
        : $"<PackageReference Include=\"{packageName}\" Version=\"{version}\" />";

    var parsedRef = Parser.ParseText(packageRefXml);
    var newPackageReference = (XmlEmptyElementSyntax)parsedRef.RootSyntax;

    XmlDocumentSyntax updatedRoot;

    if (targetItemGroup != null)
    {
      var lastPackageRef = targetItemGroup.Content
          .OfType<XmlEmptyElementSyntax>()
          .Last(c => c.IsPackageReference());

      var newPackageRefWithTrivia = newPackageReference
          .WithLeadingTrivia(SyntaxFactory.WhitespaceTrivia("\n    "));

      updatedRoot = root.InsertNodesAfter(lastPackageRef, [newPackageRefWithTrivia]);
    }
    else
    {
      var projectElement = root
          .Descendants()
          .OfType<XmlElementSyntax>()
          .FirstOrDefault(e => e.IsProject()) ?? throw new InvalidOperationException("Could not find Project element in csproj file.");

      var itemGroupXml = $"<ItemGroup>\n    {packageRefXml}\n  </ItemGroup>";
      var parsedGroup = Parser.ParseText(itemGroupXml);
      var newItemGroup = (XmlElementSyntax)parsedGroup.RootSyntax;

      var lastGroup = projectElement.Content
          .OfType<XmlElementSyntax>()
          .LastOrDefault(e => e.IsItemGroup() || e.IsPropertyGroup());

      if (lastGroup != null)
      {
        var newItemGroupWithTrivia = newItemGroup
            .WithLeadingTrivia(SyntaxFactory.WhitespaceTrivia("\n  "));

        updatedRoot = root.InsertNodesAfter(lastGroup, [newItemGroupWithTrivia]);
      }
      else
      {
        var updatedProject = projectElement.AddContent(newItemGroup);
        updatedRoot = root.ReplaceNode(projectElement, updatedProject);
      }
    }

    File.WriteAllText(csprojPath, updatedRoot.ToFullString());
  }

  private static XmlDocumentSyntax OpenCsprojFile(string csprojPath)
  {
    var original = File.ReadAllText(csprojPath);
    return Parser.ParseText(original);
  }
}

public static class XmlElementExtensions
{
  public static bool IsProject(this INamedXmlNode x) => x.Name == "Project";
  public static bool IsItemGroup(this INamedXmlNode x) => x.Name == "ItemGroup";
  public static bool IsPropertyGroup(this INamedXmlNode x) => x.Name == "PropertyGroup";
  public static bool IsPackageReference(this INamedXmlNode x) => x.Name == "PackageReference";
}