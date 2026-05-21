using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Diagnostics;

public class SingleTfmInTargetFrameworksTests
{
  private static DiagnosticsService Build() => new(new MockFileSystem());

  [Test]
  public async Task TargetFrameworks_WithSingleTfm_EmitsWarning()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <TargetFrameworks>net8.0</TargetFrameworks>\n  </PropertyGroup>\n</Project>";
    var diagnostics = Build().GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var d = diagnostics.SingleOrDefault(x => x.Code?.Value?.ToString() == DiagnosticCodes.SingleTfmInTargetFrameworks);
    await Assert.That(d).IsNotNull();
    await Assert.That(d!.Severity).IsEqualTo(DiagnosticSeverity.Warning);
    await Assert.That(d.Message).Contains("net8.0");
  }

  [Test]
  public async Task TargetFrameworks_WithMultipleTfms_NoWarning()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>\n  </PropertyGroup>\n</Project>";
    var diagnostics = Build().GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var matches = diagnostics.Where(x => x.Code?.Value?.ToString() == DiagnosticCodes.SingleTfmInTargetFrameworks).ToArray();
    await Assert.That(matches.Length).IsEqualTo(0);
  }
}