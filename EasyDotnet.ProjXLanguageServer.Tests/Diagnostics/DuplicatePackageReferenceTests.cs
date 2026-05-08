using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Diagnostics;

public class DuplicatePackageReferenceTests
{
  [Test]
  public async Task DuplicatePackageReference_EmitsErrorDiagnosticOnDuplicate()
  {
    var fs = new MockFileSystem();
    var sut = new DiagnosticsService(fs);

    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.1\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";
    var doc = Docs.Make(text, "/repo/Self/Self.csproj");

    var diagnostics = sut.GetDiagnostics(doc);
    var dupes = diagnostics.Where(d => d.Code?.Value?.ToString() == DiagnosticCodes.DuplicatePackageReference).ToArray();
    await Assert.That(dupes.Length).IsEqualTo(1);
    await Assert.That(dupes[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
    await Assert.That(dupes[0].Message).Contains("Newtonsoft.Json");
  }

  [Test]
  public async Task DuplicatePackageReferenceAcrossItemGroups_EmitsDiagnostic()
  {
    var fs = new MockFileSystem();
    var sut = new DiagnosticsService(fs);

    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Foo\" Version=\"1.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Foo\" Version=\"2.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";
    var diagnostics = sut.GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var dupes = diagnostics.Where(d => d.Code?.Value?.ToString() == DiagnosticCodes.DuplicatePackageReference).ToArray();
    await Assert.That(dupes.Length).IsEqualTo(1);
  }

  [Test]
  public async Task UniquePackageReferences_NoDiagnostic()
  {
    var fs = new MockFileSystem();
    var sut = new DiagnosticsService(fs);

    var text = "<Project>\n  <ItemGroup>\n    <PackageReference Include=\"A\" Version=\"1.0.0\" />\n    <PackageReference Include=\"B\" Version=\"1.0.0\" />\n  </ItemGroup>\n</Project>";
    var diagnostics = sut.GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var dupes = diagnostics.Where(d => d.Code?.Value?.ToString() == DiagnosticCodes.DuplicatePackageReference).ToArray();
    await Assert.That(dupes.Length).IsEqualTo(0);
  }
}