using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services.CodeActions;

internal static class RemoveElementAction
{
  public static CodeAction? BuildForDiagnostic(CsprojDocument doc, Diagnostic diagnostic)
  {
    var code = diagnostic.Code?.Value?.ToString();
    var (elementName, title) = code switch
    {
      DiagnosticCodes.MissingProjectReference => ("ProjectReference", "Remove this ProjectReference"),
      DiagnosticCodes.DuplicatePackageReference => ("PackageReference", "Remove duplicate PackageReference"),
      _ => (null, null),
    };
    if (elementName == null || title == null)
      return null;

    var startOffset = doc.ToOffset(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
    var element = AstSearch.FindElementAt(doc.Root, startOffset, elementName);
    if (element == null)
      return null;

    var node = (SyntaxNode)element;
    var deleteStart = node.SpanStart;
    var deleteEnd = node.SpanStart + node.Width;

    var lineStart = FindLineStart(doc.Text, deleteStart);
    if (IsOnlyWhitespaceBetween(doc.Text, lineStart, deleteStart))
      deleteStart = lineStart;

    var lineEnd = FindLineEnd(doc.Text, deleteEnd);
    if (lineEnd > deleteEnd && IsOnlyWhitespaceBetween(doc.Text, deleteEnd, lineEnd))
      deleteEnd = Math.Min(lineEnd + 1, doc.Text.Length);

    var edit = new TextEdit
    {
      Range = PositionUtils.ToRange(doc.LineOffsets, deleteStart, deleteEnd - deleteStart),
      NewText = string.Empty,
    };

    return CodeActionBuilder.Build(doc, title, CodeActionKind.QuickFix, diagnostic, [edit]);
  }

  private static int FindLineStart(string text, int offset)
  {
    var i = offset - 1;
    while (i >= 0 && text[i] != '\n')
      i--;
    return i + 1;
  }

  private static int FindLineEnd(string text, int offset)
  {
    var i = offset;
    while (i < text.Length && text[i] != '\n')
      i++;
    return i;
  }

  private static bool IsOnlyWhitespaceBetween(string text, int start, int end)
  {
    for (var i = start; i < end; i++)
    {
      if (!char.IsWhiteSpace(text[i]))
        return false;
    }
    return true;
  }
}