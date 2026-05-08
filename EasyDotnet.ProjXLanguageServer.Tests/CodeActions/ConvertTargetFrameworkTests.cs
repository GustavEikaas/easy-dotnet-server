using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Tests.CodeActions;

public class ConvertTargetFrameworkTests
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

  private static LspRange CursorOnTargetFramework(string text)
  {
    var idx = text.IndexOf("TargetFramework", StringComparison.Ordinal);
    var doc = Docs.Make(text);
    var lineOffsets = EasyDotnet.ProjXLanguageServer.Utils.PositionUtils.BuildLineOffsets(text);
    var pos = EasyDotnet.ProjXLanguageServer.Utils.PositionUtils.ToPosition(lineOffsets, idx);
    return new LspRange { Start = pos, End = pos };
  }

  [Test]
  public async Task TargetFramework_OffersConvertToTargetFrameworks()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n</Project>";
    var doc = Docs.Make(text);
    var actions = Sut.GetCodeActions(doc, CursorOnTargetFramework(text), []);
    var convert = actions.FirstOrDefault(a => a.Title == "Convert <TargetFramework> to <TargetFrameworks>");
    await Assert.That(convert).IsNotNull();

    var result = ApplyEdits(text, convert!.Edit!.Changes!.Values.Single());
    await Assert.That(result).Contains("<TargetFrameworks>net8.0</TargetFrameworks>");
    await Assert.That(result).DoesNotContain("<TargetFramework>");
  }

  [Test]
  public async Task SingleTfmDiagnostic_OffersConvertToTargetFramework_AndStripsExtras()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <TargetFrameworks>net8.0;</TargetFrameworks>\n  </PropertyGroup>\n</Project>";
    var doc = Docs.Make(text, "/repo/Self/Self.csproj");
    var diagnostics = new DiagnosticsService(new MockFileSystem()).GetDiagnostics(doc);
    var d = diagnostics.Single(x => x.Code?.Value?.ToString() == DiagnosticCodes.SingleTfmInTargetFrameworks);

    var actions = Sut.GetCodeActions(doc, d.Range, [d]);
    var convert = actions.FirstOrDefault(a => a.Title == "Convert <TargetFrameworks> to <TargetFramework>");
    await Assert.That(convert).IsNotNull();

    var result = ApplyEdits(text, convert!.Edit!.Changes!.Values.Single());
    await Assert.That(result).Contains("<TargetFramework>net8.0</TargetFramework>");
    await Assert.That(result).DoesNotContain("<TargetFrameworks>");
  }
}