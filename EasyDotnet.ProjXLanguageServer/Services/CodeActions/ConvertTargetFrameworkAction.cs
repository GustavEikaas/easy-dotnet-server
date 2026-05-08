using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services.CodeActions;

internal static class ConvertTargetFrameworkAction
{
  public static CodeAction? BuildToMulti(CsprojDocument doc, int rangeStart, int rangeEnd)
  {
    var element = AstSearch.FindElementOverlapping(doc.Root, rangeStart, rangeEnd, "TargetFramework");
    if (element is not XmlElementSyntax full || full.StartTag == null || full.EndTag == null)
      return null;

    var edits = CodeActionBuilder.RenameElementEdits(doc, full, "TargetFrameworks");
    if (edits == null)
      return null;

    return CodeActionBuilder.Build(doc, "Convert <TargetFramework> to <TargetFrameworks>", CodeActionKind.RefactorRewrite, null, edits.ToArray());
  }

  public static CodeAction? BuildToSingleForDiagnostic(CsprojDocument doc, Diagnostic diagnostic)
  {
    var code = diagnostic.Code?.Value?.ToString();
    if (!string.Equals(code, DiagnosticCodes.SingleTfmInTargetFrameworks, StringComparison.Ordinal))
      return null;

    var startOffset = doc.ToOffset(diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);
    var element = AstSearch.FindElementAt(doc.Root, startOffset, "TargetFrameworks");
    if (element is not XmlElementSyntax full || full.StartTag == null || full.EndTag == null)
      return null;

    var contentStart = full.StartTag.Start + full.StartTag.FullWidth;
    var contentEnd = full.EndTag.Start;
    var inner = doc.Text.Substring(contentStart, contentEnd - contentStart);
    var tfm = inner.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    if (string.IsNullOrEmpty(tfm))
      return null;

    var edits = new List<TextEdit>(CodeActionBuilder.RenameElementEdits(doc, full, "TargetFramework") ?? []);
    edits.Add(new TextEdit
    {
      Range = PositionUtils.ToRange(doc.LineOffsets, contentStart, contentEnd - contentStart),
      NewText = tfm,
    });

    return CodeActionBuilder.Build(doc, "Convert <TargetFrameworks> to <TargetFramework>", CodeActionKind.QuickFix, diagnostic, edits.ToArray());
  }
}