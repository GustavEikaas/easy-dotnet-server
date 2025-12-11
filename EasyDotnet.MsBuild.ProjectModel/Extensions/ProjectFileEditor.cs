using EasyDotnet.MsBuild.ProjectModel.Builders;
using EasyDotnet.MsBuild.ProjectModel.Syntax;
using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Extensions;

/// <summary>
/// Convenience methods for editing . csproj files with automatic file I/O.
/// </summary>
public static class ProjectFileEditor
{
  /// <summary>
  /// Adds a PackageReference to a .csproj file and saves it.
  /// </summary>
  public static void AddPackageReference(
      string csprojPath,
      string packageName,
      string? version = null)
  {
    var content = File.ReadAllText(csprojPath);
    var project = MsBuildSyntaxTree.Parse(content).Root;

    var packageGroup = project.ItemGroups
        .FirstOrDefault(ig => ig.Items.Any(i => i.Kind == MsBuildSyntaxKind.PackageReference));

    if (packageGroup != null)
    {
      var dirtyTree = packageGroup.AddPackageReference(packageName, version);
      var updatedXml = ReplaceItemGroup(content, packageGroup, dirtyTree.ToXml());
      File.WriteAllText(csprojPath, updatedXml);
    }
    else
    {
      // Insert new ItemGroup before </Project>
      var newItemGroup = new ItemGroupBuilder()
          .AddItem(b =>
          {
            b.WithItemType(MsBuildSyntaxKind.PackageReference.ToElementName())
                   .WithInclude(packageName);
            if (version != null) b.WithAttribute("Version", version);
          })
          .BuildDraft()
          .ToXml(1); // Indent level 1

      var updatedXml = content.Replace("</Project>", $"{newItemGroup}</Project>");
      File.WriteAllText(csprojPath, updatedXml);
    }
  }

  /// <summary>
  /// Adds a ProjectReference to a .csproj file and saves it.
  /// </summary>
  public static void AddProjectReference(
      string csprojPath,
      string projectPath)
  {
    var content = File.ReadAllText(csprojPath);
    var project = MsBuildSyntaxTree.Parse(content).Root;

    var projectRefGroup = project.ItemGroups
        .FirstOrDefault(ig => ig.Items.Any(i => i.Kind == MsBuildSyntaxKind.ProjectReference));

    if (projectRefGroup != null)
    {
      var dirtyTree = projectRefGroup.AddProjectReference(projectPath);
      var updatedXml = ReplaceItemGroup(content, projectRefGroup, dirtyTree.ToXml());
      File.WriteAllText(csprojPath, updatedXml);
    }
    else
    {
      // Insert new ItemGroup
      var newItemGroup = new ItemGroupBuilder()
          .AddItem(b => b
              .WithItemType(MsBuildSyntaxKind.ProjectReference.ToElementName())
              .WithInclude(projectPath))
          .BuildDraft()
          .ToXml(1);

      var updatedXml = content.Replace("</Project>", $"{newItemGroup}</Project>");
      File.WriteAllText(csprojPath, updatedXml);
    }
  }

  /// <summary>
  /// Performs multiple mutations and saves once.
  /// </summary>
  public static void EditProject(
      string csprojPath,
      Action<ProjectEditor> configure)
  {
    var content = File.ReadAllText(csprojPath);
    var editor = new ProjectEditor(content);

    configure(editor);

    File.WriteAllText(csprojPath, editor.ToXml());
  }

  private static string ReplaceItemGroup(string originalXml, ItemGroupSyntax oldGroup, string newGroupXml)
  {
    // Simple replacement - in production you'd want span-based replacement
    var oldXml = oldGroup.ToXml();
    return originalXml.Replace(oldXml, newGroupXml);
  }
}

/// <summary>
/// Fluent API for batching multiple edits before writing.
/// </summary>
public class ProjectEditor
{
  private string _currentXml;
  private ProjectSyntax _project;

  internal ProjectEditor(string xml)
  {
    _currentXml = xml;
    _project = MsBuildSyntaxTree.Parse(xml).Root;
  }

  public ProjectEditor AddPackageReference(string packageName, string? version = null)
  {
    var packageGroup = _project.ItemGroups
        .FirstOrDefault(ig => ig.Items.Any(i => i.Kind == MsBuildSyntaxKind.PackageReference));

    if (packageGroup != null)
    {
      var dirtyTree = packageGroup.AddPackageReference(packageName, version);
      var oldXml = packageGroup.ToXml();
      _currentXml = _currentXml.Replace(oldXml, dirtyTree.ToXml());
      _project = MsBuildSyntaxTree.Parse(_currentXml).Root; // Reparse
    }

    return this;
  }

  public ProjectEditor AddProjectReference(string projectPath)
  {
    var projectRefGroup = _project.ItemGroups
        .FirstOrDefault(ig => ig.Items.Any(i => i.Kind == MsBuildSyntaxKind.ProjectReference));

    if (projectRefGroup != null)
    {
      var dirtyTree = projectRefGroup.AddProjectReference(projectPath);
      var oldXml = projectRefGroup.ToXml();
      _currentXml = _currentXml.Replace(oldXml, dirtyTree.ToXml());
      _project = MsBuildSyntaxTree.Parse(_currentXml).Root;
    }

    return this;
  }

  public string ToXml() => _currentXml;
}