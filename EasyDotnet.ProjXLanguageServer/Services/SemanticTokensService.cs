using System.Text.RegularExpressions;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface ISemanticTokensService
{
  SemanticTokens GetTokens(CsprojDocument doc);
  SemanticTokensLegend Legend { get; }
}

public class SemanticTokensService : ISemanticTokensService
{
  public static readonly string[] TokenTypes =
  [
    SemanticTokenTypes.Class,        // 0 element name
    SemanticTokenTypes.Property,     // 1 attribute name
    SemanticTokenTypes.String,       // 2 attribute value / element text
    SemanticTokenTypes.Number,       // 3 version
    SemanticTokenTypes.EnumMember,   // 4 user secrets guid
    SemanticTokenTypes.Macro,        // 5 $(MsBuildVar)
    SemanticTokenTypes.Regexp,       // 6 condition value
  ];

  public static readonly string[] TokenModifiers = [];

  private const int TypeClass = 0;
  private const int TypeProperty = 1;
  private const int TypeString = 2;
  private const int TypeNumber = 3;
  private const int TypeEnumMember = 4;
  private const int TypeMacro = 5;
  private const int TypeRegexp = 6;

  private static readonly Regex GuidRegex = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
  private static readonly Regex MsBuildVarRegex = new(@"\$\([A-Za-z_][A-Za-z0-9_.]*\)", RegexOptions.Compiled);

  public SemanticTokensLegend Legend => new()
  {
    TokenTypes = TokenTypes,
    TokenModifiers = TokenModifiers,
  };

  public SemanticTokens GetTokens(CsprojDocument doc)
  {
    var raw = new List<RawToken>();
    Walk(doc.Root, doc.Text, raw);
    raw.Sort((a, b) => a.Offset.CompareTo(b.Offset));
    var data = Encode(raw, doc.LineOffsets);
    return new SemanticTokens { Data = data };
  }

  private static void Walk(SyntaxNode? node, string text, List<RawToken> tokens)
  {
    if (node == null)
    {
      return;
    }

    switch (node)
    {
      case XmlElementSyntax element:
        EmitElementName(element.StartTag?.NameNode, tokens);
        EmitElementName(element.EndTag?.NameNode, tokens);
        if (element.StartTag != null)
        {
          EmitAttributes(element.StartTag.AttributesNode, element.Name, text, tokens);
        }

        EmitElementText(element, text, tokens);
        break;

      case XmlEmptyElementSyntax empty:
        EmitElementName(empty.NameNode, tokens);
        EmitAttributes(empty.AttributesNode, empty.Name, text, tokens);
        break;
    }

    foreach (var child in node.ChildNodes)
    {
      Walk(child, text, tokens);
    }
  }

  private static void EmitElementName(XmlNameSyntax? nameNode, List<RawToken> tokens)
  {
    if (nameNode == null || nameNode.LocalNameNode == null)
    {
      return;
    }

    var local = nameNode.LocalNameNode;
    if (local.FullWidth <= 0)
    {
      return;
    }

    tokens.Add(new RawToken(local.Start, local.FullWidth, TypeClass));
  }

  private static void EmitAttributes(SyntaxList<XmlAttributeSyntax> attributes, string elementName, string text, List<RawToken> tokens)
  {
    foreach (var attr in attributes)
    {
      var attrName = attr.NameNode?.LocalNameNode;
      if (attrName?.FullWidth > 0)
      {
        tokens.Add(new RawToken(attrName.Start, attrName.FullWidth, TypeProperty));
      }

      var value = attr.ValueNode;
      if (value == null)
      {
        continue;
      }

      var startQuote = value.StartQuoteToken;
      var endQuote = value.EndQuoteToken;
      var inside = startQuote != null ? startQuote.Start + startQuote.FullWidth : value.Start;
      var insideEnd = endQuote != null ? endQuote.Start : value.Start + value.FullWidth;
      if (insideEnd <= inside || insideEnd > text.Length)
      {
        continue;
      }

      var attrType = ClassifyAttributeValue(elementName, attr.Name);
      EmitSegmentsWithMacros(text, inside, insideEnd - inside, attrType, tokens);
    }
  }

  private static int ClassifyAttributeValue(string elementName, string attrName)
  {
    if (string.Equals(attrName, "Condition", StringComparison.Ordinal))
    {
      return TypeRegexp;
    }

    if (string.Equals(elementName, "PackageReference", StringComparison.Ordinal)
        && string.Equals(attrName, "Version", StringComparison.Ordinal))
    {
      return TypeNumber;
    }

    return TypeString;
  }

  private static void EmitElementText(XmlElementSyntax element, string text, List<RawToken> tokens)
  {
    var startTag = element.StartTag;
    var endTag = element.EndTag;
    if (startTag == null || endTag == null)
    {
      return;
    }

    var contentStart = startTag.Start + startTag.FullWidth;
    var contentEnd = endTag.Start;
    if (contentEnd <= contentStart || contentEnd > text.Length)
    {
      return;
    }

    var hasChildElements = element.Elements.Any();
    if (hasChildElements)
      return;

    var inner = text[contentStart..contentEnd];
    var trimmed = inner.Trim();
    if (trimmed.Length == 0)
    {
      return;
    }

    if (string.Equals(element.Name, "UserSecretsId", StringComparison.Ordinal))
    {
      var match = GuidRegex.Match(inner);
      if (match.Success)
      {
        tokens.Add(new RawToken(contentStart + match.Index, match.Length, TypeEnumMember));
        return;
      }
    }

    var trimStart = inner.IndexOf(trimmed, StringComparison.Ordinal);
    if (trimStart < 0)
      trimStart = 0;

    EmitSegmentsWithMacros(text, contentStart + trimStart, trimmed.Length, TypeString, tokens);
  }

  private static void EmitSegmentsWithMacros(string text, int start, int length, int baseType, List<RawToken> tokens)
  {
    if (length <= 0 || start + length > text.Length)
      return;

    var slice = text.Substring(start, length);
    var matches = MsBuildVarRegex.Matches(slice);
    if (matches.Count == 0)
    {
      tokens.Add(new RawToken(start, length, baseType));
      return;
    }

    var cursor = 0;
    foreach (Match m in matches)
    {
      if (m.Index > cursor)
        tokens.Add(new RawToken(start + cursor, m.Index - cursor, baseType));
      tokens.Add(new RawToken(start + m.Index, m.Length, TypeMacro));
      cursor = m.Index + m.Length;
    }
    if (cursor < length)
      tokens.Add(new RawToken(start + cursor, length - cursor, baseType));
  }

  private static int[] Encode(List<RawToken> tokens, int[] lineOffsets)
  {
    var result = new List<int>(tokens.Count * 5);
    var prevLine = 0;
    var prevChar = 0;
    foreach (var t in tokens)
    {
      var (line, character) = OffsetToLineChar(lineOffsets, t.Offset);
      var deltaLine = line - prevLine;
      var deltaChar = deltaLine == 0 ? character - prevChar : character;
      if (deltaLine < 0 || (deltaLine == 0 && deltaChar < 0))
      {
        continue;
      }

      result.Add(deltaLine);
      result.Add(deltaChar);
      result.Add(t.Length);
      result.Add(t.TypeIndex);
      result.Add(0);
      prevLine = line;
      prevChar = character;
    }
    return [.. result];
  }

  private static (int line, int character) OffsetToLineChar(int[] lineOffsets, int offset)
  {
    var lo = 0;
    var hi = lineOffsets.Length - 1;
    while (lo < hi)
    {
      var mid = (lo + hi + 1) / 2;
      if (lineOffsets[mid] <= offset)
      {
        lo = mid;
      }
      else
      {
        hi = mid - 1;
      }
    }
    return (lo, offset - lineOffsets[lo]);
  }

  private readonly record struct RawToken(int Offset, int Length, int TypeIndex);
}