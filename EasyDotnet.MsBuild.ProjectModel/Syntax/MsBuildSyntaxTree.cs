using System.Xml.Linq;
using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax;

public sealed class MsBuildSyntaxTree
{
  public ProjectSyntax Root { get; }
  public string Text { get; }

  private MsBuildSyntaxTree(ProjectSyntax root, string text)
  {
    Root = root;
    Text = text;
  }

  public static MsBuildSyntaxTree Parse(string xml)
  {
    var doc = XDocument.Parse(xml, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
    var parser = new SyntaxTreeParser(xml);
    var root = parser.ParseProject(doc.Root!);
    return new MsBuildSyntaxTree(root, xml);
  }

  public IEnumerable<PropertySyntax> GetAllProperties() => Root.DescendantNodesOfType<PropertySyntax>();

  public IEnumerable<ItemSyntax> GetAllItems() => Root.DescendantNodesOfType<ItemSyntax>();

  public IEnumerable<ItemSyntax> GetItemsByType(string itemType) => GetAllItems().Where(i => i.ItemType == itemType);

  public PropertySyntax? FindPropertyByName(string name) => GetAllProperties().FirstOrDefault(p => p.Name == name);

  public MsBuildSyntaxNode? FindNodeAt(Position position)
  {
    var absolutePos = GetAbsolutePosition(position);
    return FindNodeAtPosition(Root, absolutePos);
  }

  private static MsBuildSyntaxNode? FindNodeAtPosition(MsBuildSyntaxNode node, int position)
  {
    if (!node.Span.Contains(position))
      return null;

    foreach (var child in node.Children)
    {
      var result = FindNodeAtPosition(child, position);
      if (result != null)
        return result;
    }

    return node;
  }

  private int GetAbsolutePosition(Position position)
  {
    var lines = Text.Split('\n');
    var pos = 0;

    for (var i = 0; i < position.Line && i < lines.Length; i++)
    {
      pos += lines[i].Length + 1;
    }

    pos += position.Character;
    return pos;
  }
}