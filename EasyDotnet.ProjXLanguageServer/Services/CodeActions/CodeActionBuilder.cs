using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services.CodeActions;

internal static class CodeActionBuilder
{
  public static CodeAction Build(CsprojDocument doc, string title, CodeActionKind kind, Diagnostic? diagnostic, TextEdit[] edits) => new()
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

  public static List<TextEdit>? RenameElementEdits(CsprojDocument doc, XmlElementSyntax element, string newName)
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
}