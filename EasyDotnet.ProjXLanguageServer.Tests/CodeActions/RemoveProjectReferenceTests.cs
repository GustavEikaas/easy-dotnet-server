using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.CodeActions;

public class RemoveProjectReferenceTests
{
  private static readonly CodeActionService Sut = new(new UserSecretsResolver(new System.IO.Abstractions.TestingHelpers.MockFileSystem()));

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
  public async Task NoDiagnostics_NoRemoveAction()
  {
    var text = "<Project>\n  <ItemGroup>\n    <ProjectReference Include=\"../X/X.csproj\" />\n  </ItemGroup>\n</Project>";
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