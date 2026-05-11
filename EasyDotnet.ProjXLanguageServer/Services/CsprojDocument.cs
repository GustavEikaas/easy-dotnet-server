using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;

namespace EasyDotnet.ProjXLanguageServer.Services;

public sealed class CsprojDocument(Uri uri, string text, int version)
{
  public Uri Uri { get; } = uri;
  public string Text { get; } = text;
  public int Version { get; } = version;
  public XmlDocumentSyntax Root { get; } = Parser.ParseText(text);
  public int[] LineOffsets { get; } = PositionUtils.BuildLineOffsets(text);

  public int ToOffset(int line, int character) => PositionUtils.ToOffset(LineOffsets, Text.Length, line, character);
}