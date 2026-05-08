using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services.CodeActions;

internal static class CollapseSelfClosingAction
{
  public static CodeAction? Build(CsprojDocument doc, int rangeStart, int rangeEnd)
  {
    var element = AstSearch.FindLast<XmlElementSyntax>(
        doc.Root,
        e => AstSearch.Overlaps(e, rangeStart, rangeEnd));
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

    if (doc.Text.Substring(bodyStart, bodyEnd - bodyStart).Trim().Length != 0)
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

    return CodeActionBuilder.Build(doc, $"Collapse <{element.Name}></{element.Name}> to <{element.Name} />", CodeActionKind.RefactorRewrite, null, [edit]);
  }
}