using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface ICodeActionService
{
  CodeAction[] GetCodeActions(CsprojDocument doc, LspRange range, Diagnostic[] contextDiagnostics);
}

public class CodeActionService : ICodeActionService
{
  public CodeAction[] GetCodeActions(CsprojDocument doc, LspRange range, Diagnostic[] contextDiagnostics)
  {
    var actions = new List<CodeAction>();

    var startOffset = doc.ToOffset(range.Start.Line, range.Start.Character);
    var endOffset = doc.ToOffset(range.End.Line, range.End.Character);

    var itemGroup = FindItemGroupCovering(doc.Root, startOffset, endOffset);
    if (itemGroup != null)
    {
      var sortAction = TrySortPackageReferences(doc, itemGroup);
      if (sortAction != null)
        actions.Add(sortAction);
    }

    foreach (var diagnostic in contextDiagnostics)
    {
      var code = diagnostic.Code?.Value?.ToString();
      if (string.Equals(code, DiagnosticCodes.MissingProjectReference, StringComparison.Ordinal))
      {
        var removal = TryRemoveProjectReference(doc, diagnostic);
        if (removal != null)
          actions.Add(removal);
      }
    }

    return [.. actions];
  }

  private static CodeAction? TryRemoveProjectReference(CsprojDocument doc, Diagnostic diagnostic)
  {
    var startOffset = doc.ToOffset(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
    var element = FindElementAt(doc.Root, startOffset, "ProjectReference");
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
      NewText = string.Empty
    };

    return new CodeAction
    {
      Title = "Remove this ProjectReference",
      Kind = CodeActionKind.QuickFix,
      Diagnostics = [diagnostic],
      Edit = new WorkspaceEdit
      {
        Changes = new Dictionary<string, TextEdit[]>
        {
          [doc.Uri.ToString()] = [edit]
        }
      }
    };
  }

  private static IXmlElementSyntax? FindElementAt(SyntaxNode root, int offset, string name)
  {
    IXmlElementSyntax? best = null;
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0)
    {
      var node = stack.Pop();
      if (node is IXmlElementSyntax e
          && string.Equals(e.Name, name, StringComparison.Ordinal)
          && offset >= node.Start && offset < node.Start + node.FullWidth)
      {
        best = e;
      }
      foreach (var child in node.ChildNodes)
        stack.Push(child);
    }
    return best;
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

  private static CodeAction? TrySortPackageReferences(CsprojDocument doc, IXmlElementSyntax itemGroup)
  {
    var refs = itemGroup.Elements
        .Where(e => string.Equals(e.Name, "PackageReference", StringComparison.Ordinal))
        .Select(e => new PkgRef(
            e,
            GetIncludeValue(e) ?? string.Empty,
            ((SyntaxNode)e).SpanStart,
            ((SyntaxNode)e).Width))
        .ToList();

    if (refs.Count < 2)
      return null;

    var sorted = refs.OrderBy(r => r.Include, StringComparer.OrdinalIgnoreCase).ToList();
    if (refs.Select(r => r.Include).SequenceEqual(sorted.Select(r => r.Include), StringComparer.Ordinal))
      return null;

    var edits = new TextEdit[refs.Count];
    for (var i = 0; i < refs.Count; i++)
    {
      var target = refs[i];
      var source = sorted[i];
      var sourceText = doc.Text.Substring(source.Start, source.Length);
      edits[i] = new TextEdit
      {
        Range = PositionUtils.ToRange(doc.LineOffsets, target.Start, target.Length),
        NewText = sourceText
      };
    }

    return new CodeAction
    {
      Title = "Sort PackageReferences alphabetically",
      Kind = CodeActionKind.RefactorRewrite,
      Edit = new WorkspaceEdit
      {
        Changes = new Dictionary<string, TextEdit[]>
        {
          [doc.Uri.ToString()] = edits
        }
      }
    };
  }

  private static IXmlElementSyntax? FindItemGroupCovering(SyntaxNode node, int rangeStart, int rangeEnd)
  {
    IXmlElementSyntax? best = null;
    Walk(node, rangeStart, rangeEnd, ref best);
    return best;
  }

  private static void Walk(SyntaxNode? node, int rangeStart, int rangeEnd, ref IXmlElementSyntax? best)
  {
    if (node == null)
      return;

    if (node is IXmlElementSyntax element
        && string.Equals(element.Name, "ItemGroup", StringComparison.Ordinal)
        && Overlaps(node, rangeStart, rangeEnd))
    {
      best = element;
    }

    foreach (var child in node.ChildNodes)
      Walk(child, rangeStart, rangeEnd, ref best);
  }

  private static bool Overlaps(SyntaxNode node, int rangeStart, int rangeEnd)
  {
    var nodeStart = node.Start;
    var nodeEnd = node.Start + node.FullWidth;
    return rangeStart < nodeEnd && rangeEnd > nodeStart;
  }

  private static string? GetIncludeValue(IXmlElementSyntax element)
  {
    foreach (var attr in element.Attributes)
    {
      if (string.Equals(attr.Name, "Include", StringComparison.Ordinal))
        return attr.Value;
    }
    return null;
  }

  private readonly record struct PkgRef(IXmlElementSyntax Element, string Include, int Start, int Length);
}
