using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services.CodeActions;

internal static class ExpandSelfClosingAction
{
  public static CodeAction? Build(CsprojDocument doc, int rangeStart, int rangeEnd)
  {
    var element = AstSearch.FindLast<XmlEmptyElementSyntax>(
        doc.Root,
        e => AstSearch.Overlaps(e, rangeStart, rangeEnd));
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

    return CodeActionBuilder.Build(doc, $"Expand <{element.Name}/> to <{element.Name}></{element.Name}>", CodeActionKind.RefactorRewrite, null, [edit]);
  }
}