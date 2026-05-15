using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.CodeActions;

public class RemoveProjectReferenceTests
{
  private static readonly CodeActionService Sut = new(new UserSecretsResolver(new System.IO.Abstractions.TestingHelpers.MockFileSystem()));

  private static Microsoft.VisualStudio.LanguageServer.Protocol.Range CursorAt(string text, string marker)
  {
    var (line, character) = Docs.PositionAt(text, marker);
    var pos = new Position { Line = line, Character = character };
    return new Microsoft.VisualStudio.LanguageServer.Protocol.Range { Start = pos, End = pos };
  }

  private static string ApplyEdits(string original, TextEdit[] edits)
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

  [Test]
  public async Task ExistingProjectReference_OffersRemoveActionWithoutDiagnostics_AndAppliedEditDeletesElement()
  {
    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <ProjectReference Include=\"../Other/Other.csproj\" />\n" +
        "    <ProjectReference @CURSORInclude=\"../Keep/Keep.csproj\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";
    var range = CursorAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var actions = Sut.GetCodeActions(Docs.Make(clean), range, []);

    var remove = actions.FirstOrDefault(a => a.Title == "Remove this ProjectReference");
    await Assert.That(remove).IsNotNull();

    var result = ApplyEdits(clean, remove!.Edit!.Changes!.Values.Single());
    await Assert.That(result).Contains("Other.csproj");
    await Assert.That(result).DoesNotContain("Keep.csproj");
  }

  [Test]
  public async Task OutsideProjectReference_NoNonDiagnosticRemoveAction()
  {
    var text = "<Project>\n  <ItemGroup>\n    @CURSOR<PackageReference Include=\"X\" Version=\"1\" />\n  </ItemGroup>\n</Project>";
    var range = CursorAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var actions = Sut.GetCodeActions(Docs.Make(clean), range, []);

    await Assert.That(actions.Any(a => a.Title == "Remove this ProjectReference")).IsFalse();
  }

  [Test]
  public async Task DiagnosticForMissingRef_OffersRemoveAction_AndAppliedEditDeletesElement()
  {
    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <ProjectReference Include=\"../Other/Other.csproj\" />\n" +
        "    <ProjectReference Include=\"../Missing/Missing.csproj\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";

    var fs = new MockFileSystem();
    fs.AddFile("/repo/Other/Other.csproj", new MockFileData("<Project/>"));
    var diagnostics = new DiagnosticsService(fs).GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    await Assert.That(diagnostics.Length).IsEqualTo(1);

    var doc = Docs.Make(text, "/repo/Self/Self.csproj");
    var actions = Sut.GetCodeActions(doc, diagnostics[0].Range, diagnostics);

    var remove = actions.FirstOrDefault(a => a.Title == "Remove this ProjectReference");
    await Assert.That(remove).IsNotNull();

    var edits = remove!.Edit!.Changes!.Values.Single();
    var result = ApplyEdits(text, edits);

    await Assert.That(result).DoesNotContain("Missing.csproj");
    await Assert.That(result).Contains("Other.csproj");
  }

  [Test]
  public async Task PackageReference_NoProjectReferenceRemoveAction()
  {
    var text = "<Project>\n  <ItemGroup>\n    <PackageReference Include=\"X\" Version=\"1\" />\n  </ItemGroup>\n</Project>";
    var doc = Docs.Make(text, "/repo/Self/Self.csproj");

    var range = new Microsoft.VisualStudio.LanguageServer.Protocol.Range
    {
      Start = new Position { Line = 2, Character = 4 },
      End = new Position { Line = 2, Character = 4 }
    };
    var actions = Sut.GetCodeActions(doc, range, []);
    await Assert.That(actions.Any(a => a.Title == "Remove this ProjectReference")).IsFalse();
  }
}
