using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;

namespace EasyDotnet.ProjXLanguageServer.Services;

public sealed class CsprojDocument
{
  public Uri Uri { get; }
  public string Text { get; }
  public int Version { get; }
  public XmlDocumentSyntax Root { get; }
  public int[] LineOffsets { get; }

  public CsprojDocument(Uri uri, string text, int version)
  {
    Uri = uri;
    Text = text;
    Version = version;
    Root = Parser.ParseText(text);
    LineOffsets = PositionUtils.BuildLineOffsets(text);
  }

  public int ToOffset(int line, int character) => PositionUtils.ToOffset(LineOffsets, Text.Length, line, character);
}
