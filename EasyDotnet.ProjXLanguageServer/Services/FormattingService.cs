using System.Text;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IFormattingService
{
  TextEdit[] Format(CsprojDocument doc, FormattingOptions options);
}

public class FormattingService : IFormattingService
{
  public TextEdit[] Format(CsprojDocument doc, FormattingOptions options)
  {
    if (doc.Root.ContainsDiagnostics)
      return [];
    if (HasMissingTags(doc.Root))
      return [];

    var indent = options.InsertSpaces
        ? new string(' ', Math.Max(2, options.TabSize))
        : "\t";

    var sb = new StringBuilder();
    EmitDocument(doc, sb, indent);
    var formatted = sb.ToString();

    if (string.Equals(formatted, doc.Text, StringComparison.Ordinal))
      return [];

    return
    [
      new TextEdit
      {
        Range = new LspRange
        {
          Start = new Position { Line = 0, Character = 0 },
          End = PositionUtils.ToPosition(doc.LineOffsets, doc.Text.Length),
        },
        NewText = formatted,
      },
    ];
  }

  private static bool HasMissingTags(SyntaxNode node)
  {
    if (node is XmlElementSyntax el)
    {
      if (el.StartTag == null || el.EndTag == null)
        return true;
      if (string.IsNullOrEmpty(el.EndTag.Name))
        return true;
      if (!string.Equals(el.StartTag.Name, el.EndTag.Name, StringComparison.Ordinal))
        return true;
    }
    foreach (var child in node.ChildNodes)
    {
      if (HasMissingTags(child))
        return true;
    }
    return false;
  }

  private static void EmitDocument(CsprojDocument doc, StringBuilder sb, string indent)
  {
    var root = doc.Root;

    if (root.Prologue != null)
    {
      sb.Append(RawSource(doc.Text, (SyntaxNode)root.Prologue).TrimEnd());
      sb.Append('\n');
    }

    foreach (var node in root.PrecedingMisc)
    {
      var raw = RawSource(doc.Text, node).Trim();
      if (raw.Length == 0)
        continue;
      sb.Append(raw);
      sb.Append('\n');
    }

    if (root.Body is IXmlElementSyntax bodyEl)
      EmitElement(doc, bodyEl, sb, indent, 0);

    foreach (var node in root.FollowingMisc)
    {
      var raw = RawSource(doc.Text, node).Trim();
      if (raw.Length == 0)
        continue;
      sb.Append('\n');
      sb.Append(raw);
    }

    if (sb.Length > 0 && sb[^1] != '\n')
      sb.Append('\n');
  }

  private static void EmitElement(CsprojDocument doc, IXmlElementSyntax element, StringBuilder sb, string indent, int depth)
  {
    var pad = Repeat(indent, depth);

    if (element is XmlEmptyElementSyntax empty)
    {
      sb.Append(pad);
      sb.Append('<');
      sb.Append(empty.Name);
      AppendAttributes(doc, empty.AttributesNode, sb);
      sb.Append(" />\n");
      return;
    }

    if (element is not XmlElementSyntax full || full.StartTag == null || full.EndTag == null)
      return;

    var childElements = full.Elements.ToList();
    var childComments = full.Content
        .Where(c => c is XmlCommentSyntax)
        .Cast<SyntaxNode>()
        .ToList();
    var hasChildren = childElements.Count > 0 || childComments.Count > 0;

    sb.Append(pad);
    sb.Append('<');
    sb.Append(full.Name);
    AppendAttributes(doc, full.StartTag.AttributesNode, sb);

    if (!hasChildren)
    {
      var text = GetInnerText(doc.Text, full).Trim();
      if (text.Length == 0)
      {
        sb.Append("></");
        sb.Append(full.Name);
        sb.Append(">\n");
        return;
      }
      sb.Append('>');
      sb.Append(text);
      sb.Append("</");
      sb.Append(full.Name);
      sb.Append(">\n");
      return;
    }

    sb.Append(">\n");

    SyntaxNode? prev = null;
    foreach (var child in full.Content)
    {
      if (child is not (IXmlElementSyntax or XmlCommentSyntax))
        continue;

      if (prev != null && HasBlankLineBetween(doc.Text, prev, child))
        sb.Append('\n');

      switch (child)
      {
        case IXmlElementSyntax childEl:
          EmitElement(doc, childEl, sb, indent, depth + 1);
          break;
        case XmlCommentSyntax comment:
          var raw = RawSource(doc.Text, comment).Trim();
          if (raw.Length > 0)
          {
            sb.Append(Repeat(indent, depth + 1));
            sb.Append(raw);
            sb.Append('\n');
          }
          break;
      }

      prev = child;
    }

    sb.Append(pad);
    sb.Append("</");
    sb.Append(full.Name);
    sb.Append(">\n");
  }

  private static void AppendAttributes(CsprojDocument doc, SyntaxList<XmlAttributeSyntax> attributes, StringBuilder sb)
  {
    foreach (var attr in attributes)
    {
      sb.Append(' ');
      sb.Append(RawSource(doc.Text, attr).Trim());
    }
  }

  private static string GetInnerText(string text, XmlElementSyntax element)
  {
    var startTag = element.StartTag!;
    var endTag = element.EndTag!;
    var contentStart = startTag.Start + startTag.FullWidth;
    var contentEnd = endTag.Start;
    if (contentEnd <= contentStart || contentEnd > text.Length)
      return string.Empty;
    return text.Substring(contentStart, contentEnd - contentStart);
  }

  private static bool HasBlankLineBetween(string text, SyntaxNode prev, SyntaxNode next)
  {
    var start = prev.SpanStart + prev.Width;
    var end = next.SpanStart;
    if (start < 0 || end > text.Length || start >= end)
      return false;
    var newlines = 0;
    for (var i = start; i < end; i++)
    {
      if (text[i] == '\n')
      {
        newlines++;
        if (newlines >= 2)
          return true;
      }
    }
    return false;
  }

  private static string RawSource(string text, SyntaxNode node)
  {
    var start = node.SpanStart;
    var len = node.Width;
    if (start < 0 || start + len > text.Length)
      return string.Empty;
    return text.Substring(start, len);
  }

  private static string Repeat(string s, int count)
  {
    if (count <= 0)
      return string.Empty;
    var sb = new StringBuilder(s.Length * count);
    for (var i = 0; i < count; i++)
      sb.Append(s);
    return sb.ToString();
  }
}