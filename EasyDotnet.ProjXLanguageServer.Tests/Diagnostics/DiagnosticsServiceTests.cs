using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Diagnostics;

public class DiagnosticsServiceTests
{
  [Test]
  public async Task MissingProjectReference_EmitsErrorDiagnostic()
  {
    var fs = new MockFileSystem();
    var sut = new DiagnosticsService(fs);

    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <ProjectReference Include=\"../Missing/Missing.csproj\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";
    var doc = Docs.Make(text, "/repo/Self/Self.csproj");

    var diagnostics = sut.GetDiagnostics(doc);
    await Assert.That(diagnostics.Length).IsEqualTo(1);
    await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
    await Assert.That(diagnostics[0].Code?.Value?.ToString()).IsEqualTo(DiagnosticCodes.MissingProjectReference);
    await Assert.That(diagnostics[0].Message).Contains("Missing.csproj");
  }

  [Test]
  public async Task ExistingProjectReference_WithBackslashSeparators_NoDiagnostic()
  {
    var fs = new MockFileSystem();
    fs.AddFile("/repo/Greendrill.Api.Models/Greendrill.Api.Models.csproj", new MockFileData("<Project/>"));
    var sut = new DiagnosticsService(fs);

    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <ProjectReference Include=\"..\\Greendrill.Api.Models\\Greendrill.Api.Models.csproj\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";
    var doc = Docs.Make(text, "/repo/Self/Self.csproj");

    var diagnostics = sut.GetDiagnostics(doc);
    await Assert.That(diagnostics.Length).IsEqualTo(0);
  }

  [Test]
  public async Task ExistingProjectReference_NoDiagnostic()
  {
    var fs = new MockFileSystem();
    fs.AddFile("/repo/Other/Other.csproj", new MockFileData("<Project/>"));
    var sut = new DiagnosticsService(fs);

    var text = "<Project>\n  <ItemGroup>\n    <ProjectReference Include=\"../Other/Other.csproj\" />\n  </ItemGroup>\n</Project>";
    var doc = Docs.Make(text, "/repo/Self/Self.csproj");

    var diagnostics = sut.GetDiagnostics(doc);
    await Assert.That(diagnostics.Length).IsEqualTo(0);
  }
}