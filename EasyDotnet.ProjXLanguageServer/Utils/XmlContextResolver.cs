using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.Language.Xml;

namespace EasyDotnet.ProjXLanguageServer.Utils;

public enum CursorContextKind
{
  Unknown,
  ProjectRoot,
  PropertyGroup,
  ItemGroup,
  InsideStartTag,
  InsideAttributeValue,
  InsideElementText,
}

public sealed record CursorContext(
    CursorContextKind Kind,
    IXmlElementSyntax? Element,
    string? ElementName,
    string? AttributeName,
    string? ParentElementName);

public static class XmlContextResolver
{
  public static CursorContext Resolve(CsprojDocument doc, int line, int character)
  {
    var position = doc.ToOffset(line, character);
    return Resolve(doc.Root, position);
  }

  public static CursorContext Resolve(XmlDocumentSyntax root, int position)
  {
    try
    {
      var node = root.FindNode(position, includeTrivia: true);
      if (node == null)
      {
        return Unknown();
      }

      var attributeName = TryFindEnclosingAttributeName(node, position);
      if (attributeName != null)
      {
        var owningElement = FindContainingElement(node);
        return new CursorContext(
            CursorContextKind.InsideAttributeValue,
            owningElement,
            owningElement?.Name,
            attributeName,
            FindParentName(owningElement));
      }

      var element = FindContainingElement(node);
      if (element == null)
      {
        return Unknown();
      }

      if (IsInsideStartTag(element, position))
      {
        var parent = element.Parent;
        return new CursorContext(
            CursorContextKind.InsideStartTag,
            element,
            element.Name,
            null,
            parent?.Name);
      }

      if (element is XmlElementSyntax elementWithBody && IsInsideElementText(elementWithBody, position))
      {
        return new CursorContext(
            CursorContextKind.InsideElementText,
            element,
            element.Name,
            null,
            FindParentName(element));
      }

      var contextKind = ClassifyByAncestor(element);
      return new CursorContext(contextKind, element, element.Name, null, FindParentName(element));
    }
    catch
    {
      return Unknown();
    }
  }

  private static CursorContext Unknown() =>
    new(CursorContextKind.Unknown, null, null, null, null);

  private static CursorContextKind ClassifyByAncestor(IXmlElementSyntax element)
  {
    var current = (IXmlElementSyntax?)element;
    while (current != null)
    {
      var name = current.Name;
      if (!string.IsNullOrEmpty(name))
      {
        return name switch
        {
          "Project" => CursorContextKind.ProjectRoot,
          "PropertyGroup" => CursorContextKind.PropertyGroup,
          "ItemGroup" => CursorContextKind.ItemGroup,
          _ => CursorContextKind.Unknown,
        };
      }
      current = current.Parent;
    }
    return CursorContextKind.Unknown;
  }

  private static string? TryFindEnclosingAttributeName(SyntaxNode? node, int position)
  {
    var current = node;
    while (current != null)
    {
      if (current is XmlAttributeSyntax attr)
      {
        var value = attr.ValueNode;
        if (value != null && position >= value.Start && position <= value.Start + value.FullWidth)
        {
          return attr.Name;
        }

        return null;
      }
      current = current.Parent;
    }
    return null;
  }

  private static IXmlElementSyntax? FindContainingElement(SyntaxNode? node)
  {
    while (node != null)
    {
      if (node is IXmlElementSyntax element)
      {
        return element;
      }

      node = node.Parent;
    }
    return null;
  }

  private static string? FindParentName(IXmlElementSyntax? element) =>
      element?.Parent?.Name;

  private static bool IsInsideStartTag(IXmlElementSyntax element, int position)
  {
    if (element is XmlElementSyntax full)
    {
      var startTag = full.StartTag;
      if (startTag == null)
      {
        return false;
      }

      return position >= startTag.Start && position < startTag.Start + startTag.FullWidth;
    }
    if (element is XmlEmptyElementSyntax empty)
    {
      return position >= empty.Start && position < empty.Start + empty.FullWidth;
    }
    return false;
  }

  private static bool IsInsideElementText(XmlElementSyntax element, int position)
  {
    var startTag = element.StartTag;
    var endTag = element.EndTag;
    if (startTag == null || endTag == null)
    {
      return false;
    }

    var contentStart = startTag.Start + startTag.FullWidth;
    var contentEnd = endTag.Start;
    return position >= contentStart && position <= contentEnd;
  }
}