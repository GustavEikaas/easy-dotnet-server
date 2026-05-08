using Microsoft.Language.Xml;

namespace EasyDotnet.ProjXLanguageServer.Utils;

public static class AstSearch
{
  public static IEnumerable<SyntaxNode> Descendants(SyntaxNode root)
  {
    yield return root;
    foreach (var child in root.ChildNodes)
    {
      foreach (var descendant in Descendants(child))
        yield return descendant;
    }
  }

  public static IEnumerable<IXmlElementSyntax> Elements(SyntaxNode root) =>
      Descendants(root).OfType<IXmlElementSyntax>();

  public static SyntaxNode? FindLast(SyntaxNode root, Func<SyntaxNode, bool> predicate)
  {
    SyntaxNode? best = null;
    foreach (var node in Descendants(root))
    {
      if (predicate(node))
        best = node;
    }
    return best;
  }

  public static T? FindLast<T>(SyntaxNode root, Func<T, bool> predicate) where T : class
  {
    T? best = null;
    foreach (var node in Descendants(root))
    {
      if (node is T t && predicate(t))
        best = t;
    }
    return best;
  }

  public static bool Overlaps(SyntaxNode node, int rangeStart, int rangeEnd)
  {
    var nodeStart = node.Start;
    var nodeEnd = node.Start + node.FullWidth;
    return rangeStart < nodeEnd && rangeEnd > nodeStart;
  }

  public static bool Contains(SyntaxNode node, int offset) =>
      offset >= node.Start && offset < node.Start + node.FullWidth;

  public static IXmlElementSyntax? FindElementOverlapping(SyntaxNode root, int rangeStart, int rangeEnd, string? name = null) =>
      FindLast<IXmlElementSyntax>(root, e =>
          (name == null || string.Equals(e.Name, name, StringComparison.Ordinal))
          && Overlaps((SyntaxNode)e, rangeStart, rangeEnd));

  public static IXmlElementSyntax? FindElementAt(SyntaxNode root, int offset, string name) =>
      FindLast<IXmlElementSyntax>(root, e =>
          string.Equals(e.Name, name, StringComparison.Ordinal)
          && Contains((SyntaxNode)e, offset));
}