using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Diagnostics;

public class MismatchedTagTests
{
  private static DiagnosticsService Build() => new(new MockFileSystem());

  [Test]
  public async Task MismatchedClosingTag_EmitsError()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <Target>abc</Targets>\n  </PropertyGroup>\n</Project>";
    var diagnostics = Build().GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var d = diagnostics.SingleOrDefault(x => x.Code?.Value?.ToString() == DiagnosticCodes.MismatchedTagNames);
    await Assert.That(d).IsNotNull();
    await Assert.That(d!.Severity).IsEqualTo(DiagnosticSeverity.Error);
    await Assert.That(d.Message).Contains("Target");
    await Assert.That(d.Message).Contains("Targets");
  }

  [Test]
  public async Task MatchingTags_NoDiagnostic()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <Target>abc</Target>\n  </PropertyGroup>\n</Project>";
    var diagnostics = Build().GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var matches = diagnostics.Where(x => x.Code?.Value?.ToString() == DiagnosticCodes.MismatchedTagNames).ToArray();
    await Assert.That(matches.Length).IsEqualTo(0);
  }
}