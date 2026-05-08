using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Tests.CodeActions;

public class OpenSecretsTests
{
  private static LspRange CursorAt(string text, string marker)
  {
    var doc = Docs.Make(text.Replace(marker, string.Empty));
    var idx = text.IndexOf(marker, StringComparison.Ordinal);
    var line = 0;
    var lastNl = -1;
    for (var i = 0; i < idx; i++)
    {
      if (text[i] == '\n')
      {
        line++;
        lastNl = i;
      }
    }
    var pos = new Position { Line = line, Character = idx - lastNl - 1 };
    return new LspRange { Start = pos, End = pos };
  }

  [Test]
  public async Task ValidUserSecretsId_OffersOpenSecretsAction_WithCommand()
  {
    var fs = new MockFileSystem();
    var sut = new CodeActionService(new UserSecretsResolver(fs));

    var guid = "12345678-1234-1234-1234-123456789abc";
    var text = $"<Project>\n  <PropertyGroup>\n    <UserSecretsId>@CURSOR{guid}</UserSecretsId>\n  </PropertyGroup>\n</Project>";
    var range = CursorAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var actions = sut.GetCodeActions(Docs.Make(clean), range, []);
    var open = actions.FirstOrDefault(a => a.Title == "Open secrets.json");
    await Assert.That(open).IsNotNull();
    await Assert.That(open!.Command).IsNotNull();
    await Assert.That(open.Command!.CommandIdentifier).IsEqualTo("easy-dotnet.openFile");
    var argPath = (string)open.Command.Arguments![0]!;
    await Assert.That(argPath).Contains(guid);
    await Assert.That(argPath).EndsWith("secrets.json");
    await Assert.That(fs.File.Exists(argPath)).IsTrue();
  }

  [Test]
  public async Task NonGuidUserSecretsId_OffersNoAction()
  {
    var fs = new MockFileSystem();
    var sut = new CodeActionService(new UserSecretsResolver(fs));

    var text = "<Project>\n  <PropertyGroup>\n    <UserSecretsId>not-a-guid</UserSecretsId>\n  </PropertyGroup>\n</Project>";
    var range = new LspRange { Start = new Position { Line = 2, Character = 20 }, End = new Position { Line = 2, Character = 20 } };
    var actions = sut.GetCodeActions(Docs.Make(text), range, []);
    await Assert.That(actions.Any(a => a.Title == "Open secrets.json")).IsFalse();
  }
}