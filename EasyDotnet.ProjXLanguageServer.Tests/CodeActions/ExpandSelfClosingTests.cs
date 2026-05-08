using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Tests.CodeActions;

public class ExpandSelfClosingTests
{
  private static readonly CodeActionService Sut = new(new UserSecretsResolver(new MockFileSystem()));

  private static string Apply(string original, TextEdit[] edits)
  {
    var doc = Docs.Make(original);
    var ordered = edits
        .Select(e => (start: doc.ToOffset(e.Range.Start.Line, e.Range.Start.Character),
                       end: doc.ToOffset(e.Range.End.Line, e.Range.End.Character),
                       text: e.NewText))
        .OrderByDescending(e => e.start)
        .ToList();
    var result = original;
    foreach (var (start, end, text) in ordered)
      result = string.Concat(result.AsSpan(0, start), text, result.AsSpan(end));
    return result;
  }

  private static LspRange CursorAt(string text, string marker)
  {
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
  public async Task SelfClosingPackageReference_OffersExpandAction()
  {
    var text = "<Project>\n  <ItemGroup>\n    <PackageReference @CURSORInclude=\"X\" Version=\"1\" />\n  </ItemGroup>\n</Project>";
    var range = CursorAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var actions = Sut.GetCodeActions(Docs.Make(clean), range, []);
    var expand = actions.FirstOrDefault(a => a.Title.StartsWith("Expand <PackageReference"));
    await Assert.That(expand).IsNotNull();

    var result = Apply(clean, expand!.Edit!.Changes!.Values.Single());
    await Assert.That(result).Contains("<PackageReference Include=\"X\" Version=\"1\"></PackageReference>");
    await Assert.That(result).DoesNotContain("/>");
  }

  [Test]
  public async Task EmptyNonSelfClosingPackageReference_OffersCollapseAction()
  {
    var text = "<Project>\n  <ItemGroup>\n    <PackageReference @CURSORInclude=\"X\" Version=\"1\"></PackageReference>\n  </ItemGroup>\n</Project>";
    var range = CursorAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var actions = Sut.GetCodeActions(Docs.Make(clean), range, []);
    var collapse = actions.FirstOrDefault(a => a.Title.StartsWith("Collapse <PackageReference"));
    await Assert.That(collapse).IsNotNull();

    var result = Apply(clean, collapse!.Edit!.Changes!.Values.Single());
    await Assert.That(result).Contains("<PackageReference Include=\"X\" Version=\"1\" />");
    await Assert.That(result).DoesNotContain("</PackageReference>");
  }

  [Test]
  public async Task NonEmptyElement_NoCollapseAction()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>";
    var range = new LspRange { Start = new Position { Line = 2, Character = 8 }, End = new Position { Line = 2, Character = 8 } };
    var actions = Sut.GetCodeActions(Docs.Make(text), range, []);
    await Assert.That(actions.Any(a => a.Title.StartsWith("Collapse "))).IsFalse();
  }

  [Test]
  public async Task NonSelfClosingElement_NoExpandAction()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>";
    var range = new LspRange { Start = new Position { Line = 2, Character = 8 }, End = new Position { Line = 2, Character = 8 } };
    var actions = Sut.GetCodeActions(Docs.Make(text), range, []);
    await Assert.That(actions.Any(a => a.Title.StartsWith("Expand "))).IsFalse();
  }
}