using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface ICodeActionService
{
  CodeAction[] GetCodeActions(CsprojDocument doc, LspRange range, Diagnostic[] contextDiagnostics);
}

public class CodeActionService(IUserSecretsResolver userSecretsResolver) : ICodeActionService
{
  private static readonly System.Text.RegularExpressions.Regex GuidRegex = new(
      @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
      System.Text.RegularExpressions.RegexOptions.Compiled);

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

    var tfmConvert = TryConvertTargetFramework(doc, startOffset, endOffset);
    if (tfmConvert != null)
      actions.Add(tfmConvert);

    var openSecrets = TryOpenSecrets(doc, startOffset, endOffset);
    if (openSecrets != null)
      actions.Add(openSecrets);

    var expandSelfClosing = TryExpandSelfClosing(doc, startOffset, endOffset);
    if (expandSelfClosing != null)
      actions.Add(expandSelfClosing);

    var collapseSelfClosing = TryCollapseToSelfClosing(doc, startOffset, endOffset);
    if (collapseSelfClosing != null)
      actions.Add(collapseSelfClosing);

    foreach (var diagnostic in contextDiagnostics)
    {
      var code = diagnostic.Code?.Value?.ToString();
      if (string.Equals(code, DiagnosticCodes.MissingProjectReference, StringComparison.Ordinal))
      {
        var removal = TryRemoveElement(doc, diagnostic, "ProjectReference", "Remove this ProjectReference");
        if (removal != null)
          actions.Add(removal);
      }
      else if (string.Equals(code, DiagnosticCodes.DuplicatePackageReference, StringComparison.Ordinal))
      {
        var removal = TryRemoveElement(doc, diagnostic, "PackageReference", "Remove duplicate PackageReference");
        if (removal != null)
          actions.Add(removal);
      }
      else if (string.Equals(code, DiagnosticCodes.SingleTfmInTargetFrameworks, StringComparison.Ordinal))
      {
        var convert = TryConvertSingleTfmToFramework(doc, diagnostic);
        if (convert != null)
          actions.Add(convert);
      }
    }

    return [.. actions];
  }

  private static CodeAction? TryRemoveElement(CsprojDocument doc, Diagnostic diagnostic, string elementName, string title)
  {
    var startOffset = doc.ToOffset(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
    var element = FindElementAt(doc.Root, startOffset, elementName);
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
      Title = title,
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

  private CodeAction? TryOpenSecrets(CsprojDocument doc, int rangeStart, int rangeEnd)
  {
    var element = FindElementOverlapping(doc.Root, rangeStart, rangeEnd, "UserSecretsId");
    if (element is not XmlElementSyntax full || full.StartTag == null || full.EndTag == null)
      return null;

    var contentStart = full.StartTag.Start + full.StartTag.FullWidth;
    var contentEnd = full.EndTag.Start;
    if (contentEnd <= contentStart || contentEnd > doc.Text.Length)
      return null;

    var inner = doc.Text.Substring(contentStart, contentEnd - contentStart).Trim();
    if (!GuidRegex.IsMatch(inner))
      return null;

    var path = userSecretsResolver.EnsureSecretsFile(inner);
    return new CodeAction
    {
      Title = "Open secrets.json",
      Kind = CodeActionKind.QuickFix,
      Command = new Command
      {
        Title = "Open secrets.json",
        CommandIdentifier = "easy-dotnet.openFile",
        Arguments = [path],
      },
    };
  }

  private static CodeAction? TryExpandSelfClosing(CsprojDocument doc, int rangeStart, int rangeEnd)
  {
    var element = FindEmptyElementOverlapping(doc.Root, rangeStart, rangeEnd);
    if (element == null)
      return null;

    var slash = element.SlashGreaterThanToken;
    if (slash == null)
      return null;

    var replaceStart = slash.Start;
    var replaceEnd = slash.Start + slash.FullWidth;
    while (replaceStart > 0 && char.IsWhiteSpace(doc.Text[replaceStart - 1]))
      replaceStart--;
    var edit = new TextEdit
    {
      Range = PositionUtils.ToRange(doc.LineOffsets, replaceStart, replaceEnd - replaceStart),
      NewText = $"></{element.Name}>",
    };

    return new CodeAction
    {
      Title = $"Expand <{element.Name}/> to <{element.Name}></{element.Name}>",
      Kind = CodeActionKind.RefactorRewrite,
      Edit = new WorkspaceEdit
      {
        Changes = new Dictionary<string, TextEdit[]>
        {
          [doc.Uri.ToString()] = [edit],
        },
      },
    };
  }

  private static CodeAction? TryCollapseToSelfClosing(CsprojDocument doc, int rangeStart, int rangeEnd)
  {
    var element = FindFullElementOverlapping(doc.Root, rangeStart, rangeEnd);
    if (element?.StartTag == null || element.EndTag == null)
      return null;

    if (element.Elements.Any())
      return null;
    if (element.Content.Any(c => c is XmlCommentSyntax))
      return null;

    var bodyStart = element.StartTag.Start + element.StartTag.FullWidth;
    var bodyEnd = element.EndTag.Start;
    if (bodyEnd < bodyStart || bodyEnd > doc.Text.Length)
      return null;

    var inner = doc.Text.Substring(bodyStart, bodyEnd - bodyStart);
    if (inner.Trim().Length != 0)
      return null;

    var gt = element.StartTag.GreaterThanToken;
    if (gt == null)
      return null;

    var replaceStart = gt.Start;
    var replaceEnd = element.EndTag.Start + element.EndTag.FullWidth;

    var edit = new TextEdit
    {
      Range = PositionUtils.ToRange(doc.LineOffsets, replaceStart, replaceEnd - replaceStart),
      NewText = " />",
    };

    return new CodeAction
    {
      Title = $"Collapse <{element.Name}></{element.Name}> to <{element.Name} />",
      Kind = CodeActionKind.RefactorRewrite,
      Edit = new WorkspaceEdit
      {
        Changes = new Dictionary<string, TextEdit[]>
        {
          [doc.Uri.ToString()] = [edit],
        },
      },
    };
  }

  private static XmlElementSyntax? FindFullElementOverlapping(SyntaxNode root, int rangeStart, int rangeEnd)
  {
    XmlElementSyntax? best = null;
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0)
    {
      var node = stack.Pop();
      if (node is XmlElementSyntax el)
      {
        var nodeStart = node.Start;
        var nodeEnd = node.Start + node.FullWidth;
        if (rangeStart < nodeEnd && rangeEnd > nodeStart)
          best = el;
      }
      foreach (var child in node.ChildNodes)
        stack.Push(child);
    }
    return best;
  }

  private static XmlEmptyElementSyntax? FindEmptyElementOverlapping(SyntaxNode root, int rangeStart, int rangeEnd)
  {
    XmlEmptyElementSyntax? best = null;
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0)
    {
      var node = stack.Pop();
      if (node is XmlEmptyElementSyntax empty)
      {
        var nodeStart = node.Start;
        var nodeEnd = node.Start + node.FullWidth;
        if (rangeStart < nodeEnd && rangeEnd > nodeStart)
          best = empty;
      }
      foreach (var child in node.ChildNodes)
        stack.Push(child);
    }
    return best;
  }

  private static CodeAction? TryConvertTargetFramework(CsprojDocument doc, int rangeStart, int rangeEnd)
  {
    var element = FindElementOverlapping(doc.Root, rangeStart, rangeEnd, "TargetFramework");
    if (element is not XmlElementSyntax full || full.StartTag == null || full.EndTag == null)
      return null;

    var edits = RenameElementEdits(doc, full, "TargetFrameworks");
    if (edits == null)
      return null;

    return BuildCodeAction(doc, "Convert <TargetFramework> to <TargetFrameworks>", CodeActionKind.RefactorRewrite, null, edits.ToArray());
  }

  private static CodeAction? TryConvertSingleTfmToFramework(CsprojDocument doc, Diagnostic diagnostic)
  {
    var startOffset = doc.ToOffset(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
    var element = FindElementAt(doc.Root, startOffset, "TargetFrameworks");
    if (element is not XmlElementSyntax full || full.StartTag == null || full.EndTag == null)
      return null;

    var contentStart = full.StartTag.Start + full.StartTag.FullWidth;
    var contentEnd = full.EndTag.Start;
    var inner = doc.Text.Substring(contentStart, contentEnd - contentStart);
    var tfm = inner.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    if (string.IsNullOrEmpty(tfm))
      return null;

    var edits = new List<TextEdit>(RenameElementEdits(doc, full, "TargetFramework") ?? []);
    edits.Add(new TextEdit
    {
      Range = PositionUtils.ToRange(doc.LineOffsets, contentStart, contentEnd - contentStart),
      NewText = tfm,
    });

    return BuildCodeAction(doc, "Convert <TargetFrameworks> to <TargetFramework>", CodeActionKind.QuickFix, diagnostic, edits.ToArray());
  }

  private static List<TextEdit>? RenameElementEdits(CsprojDocument doc, XmlElementSyntax element, string newName)
  {
    var startName = element.StartTag?.NameNode?.LocalNameNode;
    var endName = element.EndTag?.NameNode?.LocalNameNode;
    if (startName == null || endName == null)
      return null;

    return
    [
      new TextEdit
      {
        Range = PositionUtils.ToRange(doc.LineOffsets, startName.Start, startName.FullWidth),
        NewText = newName,
      },
      new TextEdit
      {
        Range = PositionUtils.ToRange(doc.LineOffsets, endName.Start, endName.FullWidth),
        NewText = newName,
      },
    ];
  }

  private static CodeAction BuildCodeAction(CsprojDocument doc, string title, CodeActionKind kind, Diagnostic? diagnostic, TextEdit[] edits) => new()
  {
    Title = title,
    Kind = kind,
    Diagnostics = diagnostic == null ? null : [diagnostic],
    Edit = new WorkspaceEdit
    {
      Changes = new Dictionary<string, TextEdit[]>
      {
        [doc.Uri.ToString()] = edits,
      },
    },
  };

  private static IXmlElementSyntax? FindElementOverlapping(SyntaxNode root, int rangeStart, int rangeEnd, string name)
  {
    IXmlElementSyntax? best = null;
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0)
    {
      var node = stack.Pop();
      if (node is IXmlElementSyntax e
          && string.Equals(e.Name, name, StringComparison.Ordinal))
      {
        var nodeStart = node.Start;
        var nodeEnd = node.Start + node.FullWidth;
        if (rangeStart < nodeEnd && rangeEnd > nodeStart)
          best = e;
      }
      foreach (var child in node.ChildNodes)
        stack.Push(child);
    }
    return best;
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
