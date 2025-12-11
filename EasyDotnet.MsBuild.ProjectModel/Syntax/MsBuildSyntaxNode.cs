using System.Collections.Immutable;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax;

public abstract record MsBuildSyntaxNode
{
  public required MsBuildSyntaxKind Kind { get; init; }
  public required TextSpan Span { get; init; }
  public MsBuildSyntaxNode? Parent { get; internal set; }
  public virtual ImmutableArray<MsBuildSyntaxNode> Children { get; init; } = [];

  public IEnumerable<MsBuildSyntaxNode> DescendantNodes()
  {
    foreach (var child in Children)
    {
      yield return child;
      foreach (var descendant in child.DescendantNodes())
      {
        yield return descendant;
      }
    }
  }

  public IEnumerable<T> DescendantNodesOfType<T>() where T : MsBuildSyntaxNode => DescendantNodes().OfType<T>();

  public IEnumerable<MsBuildSyntaxNode> Ancestors()
  {
    var current = Parent;
    while (current != null)
    {
      yield return current;
      current = current.Parent;
    }
  }

  public IEnumerable<MsBuildSyntaxNode> AncestorsAndSelf()
  {
    yield return this;
    foreach (var ancestor in Ancestors())
    {
      yield return ancestor;
    }
  }

  public T? FirstAncestorOfType<T>() where T : MsBuildSyntaxNode => Ancestors().OfType<T>().FirstOrDefault();

  public bool IsDescendantOf(MsBuildSyntaxNode node) => Ancestors().Contains(node);
}