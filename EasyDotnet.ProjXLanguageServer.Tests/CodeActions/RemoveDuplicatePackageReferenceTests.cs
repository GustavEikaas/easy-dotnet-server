using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.CodeActions;

public class RemoveDuplicatePackageReferenceTests
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
  public async Task DuplicateDiagnostic_OffersRemoveAction_ThatDeletesDuplicateOnly()
  {
    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.1\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";

    var fs = new MockFileSystem();
    var doc = Docs.Make(text, "/repo/Self/Self.csproj");
    var diagnostics = new DiagnosticsService(fs).GetDiagnostics(doc);
    var dup = diagnostics.Single(d => d.Code?.Value?.ToString() == DiagnosticCodes.DuplicatePackageReference);

    var actions = Sut.GetCodeActions(doc, dup.Range, [dup]);
    var removal = actions.FirstOrDefault(a => a.Title == "Remove duplicate PackageReference");
    await Assert.That(removal).IsNotNull();

    var edits = removal!.Edit!.Changes!.Values.Single();
    var result = ApplyEdits(text, edits);

    await Assert.That(result).Contains("Version=\"13.0.3\"");
    await Assert.That(result).DoesNotContain("Version=\"13.0.1\"");
  }
}