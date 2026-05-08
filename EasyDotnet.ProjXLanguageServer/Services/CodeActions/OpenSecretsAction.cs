using System.Text.RegularExpressions;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services.CodeActions;

internal static partial class OpenSecretsAction
{
  [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled)]
  private static partial Regex GuidRegex();

  public static CodeAction? Build(CsprojDocument doc, int rangeStart, int rangeEnd, IUserSecretsResolver userSecretsResolver)
  {
    var element = AstSearch.FindElementOverlapping(doc.Root, rangeStart, rangeEnd, "UserSecretsId");
    if (element is not XmlElementSyntax full || full.StartTag == null || full.EndTag == null)
      return null;

    var contentStart = full.StartTag.Start + full.StartTag.FullWidth;
    var contentEnd = full.EndTag.Start;
    if (contentEnd <= contentStart || contentEnd > doc.Text.Length)
      return null;

    var inner = doc.Text.Substring(contentStart, contentEnd - contentStart).Trim();
    if (!GuidRegex().IsMatch(inner))
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
}