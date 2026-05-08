using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Diagnostics;

public class ConflictingTargetFrameworkTests
{
  private static DiagnosticsService Build() => new(new MockFileSystem());

  [Test]
  public async Task BothUnconditional_EmitsErrorOnEach()
  {
    var text =
        "<Project>\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net8.0</TargetFramework>\n" +
        "    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>\n" +
        "  </PropertyGroup>\n" +
        "</Project>";
    var diagnostics = Build().GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var matches = diagnostics.Where(d => d.Code?.Value?.ToString() == DiagnosticCodes.ConflictingTargetFrameworkProperties).ToArray();
    await Assert.That(matches.Length).IsEqualTo(2);
    foreach (var m in matches)
      await Assert.That(m.Severity).IsEqualTo(DiagnosticSeverity.Error);
  }

  [Test]
  public async Task TargetFrameworkInsideConditionalGroup_NoConflict()
  {
    var text =
        "<Project>\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>\n" +
        "  </PropertyGroup>\n" +
        "  <PropertyGroup Condition=\"'$(BuildingForLegacy)'=='true'\">\n" +
        "    <TargetFramework>net48</TargetFramework>\n" +
        "  </PropertyGroup>\n" +
        "</Project>";
    var diagnostics = Build().GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var matches = diagnostics.Where(d => d.Code?.Value?.ToString() == DiagnosticCodes.ConflictingTargetFrameworkProperties).ToArray();
    await Assert.That(matches.Length).IsEqualTo(0);
  }

  [Test]
  public async Task ElementLevelCondition_NoConflict()
  {
    var text =
        "<Project>\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>\n" +
        "    <TargetFramework Condition=\"'$(X)'=='1'\">net48</TargetFramework>\n" +
        "  </PropertyGroup>\n" +
        "</Project>";
    var diagnostics = Build().GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var matches = diagnostics.Where(d => d.Code?.Value?.ToString() == DiagnosticCodes.ConflictingTargetFrameworkProperties).ToArray();
    await Assert.That(matches.Length).IsEqualTo(0);
  }

  [Test]
  public async Task OnlyTargetFrameworks_NoConflict()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>\n  </PropertyGroup>\n</Project>";
    var diagnostics = Build().GetDiagnostics(Docs.Make(text, "/repo/Self/Self.csproj"));
    var matches = diagnostics.Where(d => d.Code?.Value?.ToString() == DiagnosticCodes.ConflictingTargetFrameworkProperties).ToArray();
    await Assert.That(matches.Length).IsEqualTo(0);
  }
}