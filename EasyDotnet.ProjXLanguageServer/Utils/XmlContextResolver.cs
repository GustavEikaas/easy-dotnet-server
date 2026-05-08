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
    var ctx = Resolve(doc.Root, position);
    if (NeedsTextualFallback(ctx))
    {
      var fallback = TextualFallback(doc.Text, position);
      if (fallback != null)
        return fallback;
    }
    return ctx;
  }

  private static bool NeedsTextualFallback(CursorContext ctx)
  {
    if (ctx.Kind == CursorContextKind.Unknown)
      return true;
    if (ctx.Kind == CursorContextKind.InsideStartTag)
    {
      var parent = ctx.ParentElementName;
      return parent is not ("Project" or "PropertyGroup" or "ItemGroup");
    }
    if (ctx.Kind == CursorContextKind.InsideElementText)
    {
      var name = ctx.ElementName;
      if (name == null)
        return true;
      if (name is "Project" or "PropertyGroup" or "ItemGroup")
        return true;
      return !EasyDotnet.MsBuild.MsBuildProperties.GetAllPropertyNames().Any(n => string.Equals(n, name, StringComparison.Ordinal));
    }
    return false;
  }

  private static CursorContext? TextualFallback(string text, int position)
  {
    var enclosing = FindEnclosingByText(text, Math.Min(position, text.Length));
    if (enclosing == null)
      return null;

    var (name, parent) = enclosing.Value;
    var kind = name switch
    {
      "Project" => CursorContextKind.ProjectRoot,
      "PropertyGroup" => CursorContextKind.PropertyGroup,
      "ItemGroup" => CursorContextKind.ItemGroup,
      _ => CursorContextKind.InsideStartTag,
    };
    return new CursorContext(kind, null, name, null, parent);
  }

  private static (string name, string? parent)? FindEnclosingByText(string text, int upTo)
  {
    var stack = new List<string>();
    var i = 0;
    while (i < upTo)
    {
      var lt = text.IndexOf('<', i);
      if (lt < 0 || lt >= upTo)
        break;

      if (lt + 1 < upTo && text[lt + 1] == '!')
      {
        var endComment = text.IndexOf("-->", lt + 1, StringComparison.Ordinal);
        i = endComment < 0 || endComment >= upTo ? upTo : endComment + 3;
        continue;
      }

      if (lt + 1 < upTo && text[lt + 1] == '?')
      {
        var endPi = text.IndexOf("?>", lt + 1, StringComparison.Ordinal);
        i = endPi < 0 || endPi >= upTo ? upTo : endPi + 2;
        continue;
      }

      var isClose = lt + 1 < upTo && text[lt + 1] == '/';
      var nameStart = lt + (isClose ? 2 : 1);
      var nameEnd = nameStart;
      while (nameEnd < upTo && IsTagNameChar(text[nameEnd]))
        nameEnd++;

      if (nameEnd == nameStart)
      {
        i = lt + 1;
        continue;
      }

      var tagName = text.Substring(nameStart, nameEnd - nameStart);
      var gt = text.IndexOf('>', nameEnd);
      if (gt < 0 || gt >= upTo)
        break;

      var selfClosing = gt > 0 && text[gt - 1] == '/';

      if (isClose)
      {
        for (var s = stack.Count - 1; s >= 0; s--)
        {
          if (string.Equals(stack[s], tagName, StringComparison.Ordinal))
          {
            stack.RemoveRange(s, stack.Count - s);
            break;
          }
        }
      }
      else if (!selfClosing)
      {
        stack.Add(tagName);
      }

      i = gt + 1;
    }

    if (stack.Count == 0)
      return null;
    var top = stack[^1];
    var parent = stack.Count >= 2 ? stack[^2] : null;
    return (top, parent);
  }

  private static bool IsTagNameChar(char c) =>
      char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';

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