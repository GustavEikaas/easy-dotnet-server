using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax;

/// <summary>
/// Represents a modified syntax tree that needs to be rebuilt to recalculate spans.
/// </summary>
public class DirtyProjectSyntax
{
  private readonly MsBuildSyntaxNode? _originalNode;
  private readonly MsBuildSyntaxNodeDraft _modifiedDraft;
  private readonly ProjectSyntax? _originalProject;

  // Constructor for ItemGroup/PropertyGroup level mutations
  public DirtyProjectSyntax(MsBuildSyntaxNode originalNode, MsBuildSyntaxNodeDraft modifiedDraft)
  {
    _originalNode = originalNode;
    _modifiedDraft = modifiedDraft;
  }

  // Constructor for Project-level mutations
  public DirtyProjectSyntax(ProjectSyntax originalProject, ProjectSyntaxDraft modifiedDraft)
  {
    _originalProject = originalProject;
    _modifiedDraft = modifiedDraft;
  }

  /// <summary>
  /// Rebuilds the tree by serializing to XML and reparsing to get accurate spans.  
  /// </summary>
  public ProjectSyntax Rebuild()
  {
    var xml = _modifiedDraft.ToXml();

    // If it's a full project, parse directly
    if (_modifiedDraft is ProjectSyntaxDraft)
    {
      return MsBuildSyntaxTree.Parse(xml).Root;
    }

    // Otherwise wrap in Project tags
    var fullXml = _modifiedDraft switch
    {
      ItemGroupSyntaxDraft => $"<Project>{xml}</Project>",
      PropertyGroupSyntaxDraft => $"<Project>{xml}</Project>",
      _ => xml
    };

    var tree = MsBuildSyntaxTree.Parse(fullXml);
    return tree.Root;
  }

  /// <summary>
  /// Gets the XML representation without rebuilding the tree.
  /// Spans will not be available.  
  /// </summary>
  public string ToXml() => _modifiedDraft.ToXml();
}