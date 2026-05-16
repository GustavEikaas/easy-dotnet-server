using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;

namespace EasyDotnet.ProjXLanguageServer.Tests.Diagnostics;

public sealed class CentralPackageManagementDiagnosticsTests
{
  [Test]
  public async Task PackageReferenceMissingCentralPackageVersion_EmitsWarning()
  {
    const string text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";
    var sut = Build([]);

    var diagnostics = await sut.GetDiagnosticsAsync(Docs.Make(text, "/repo/App.csproj"), CancellationToken.None);

    await Assert.That(diagnostics.Length).IsEqualTo(1);
    await Assert.That(diagnostics[0].Code?.Value?.ToString()).IsEqualTo(DiagnosticCodes.MissingCentralPackageVersion);
    await Assert.That(diagnostics[0].Message).Contains("Newtonsoft.Json");
  }

  [Test]
  public async Task PackageReferenceDeclaredInCentralPackageVersion_EmitsNoWarning()
  {
    const string text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";
    var sut = Build([new CentralPackageVersionInfo("Newtonsoft.Json", "13.0.3")]);

    var diagnostics = await sut.GetDiagnosticsAsync(Docs.Make(text, "/repo/App.csproj"), CancellationToken.None);

    await Assert.That(diagnostics.Length).IsEqualTo(0);
  }

  [Test]
  public async Task PackageReferenceWithVersionInCentralPackageManagement_EmitsWarning()
  {
    const string text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";
    var sut = Build([new CentralPackageVersionInfo("Newtonsoft.Json", "13.0.3")]);

    var diagnostics = await sut.GetDiagnosticsAsync(Docs.Make(text, "/repo/App.csproj"), CancellationToken.None);

    await Assert.That(diagnostics.Length).IsEqualTo(1);
    await Assert.That(diagnostics[0].Code?.Value?.ToString()).IsEqualTo(DiagnosticCodes.MissingCentralPackageVersion);
    await Assert.That(diagnostics[0].Message).Contains("should not specify Version");
  }

  private static DiagnosticsService Build(IReadOnlyList<CentralPackageVersionInfo> centralPackages) =>
      new(
          new MockFileSystem(new Dictionary<string, MockFileData> { ["/repo/App.csproj"] = new("<Project />") }),
          new FakeHierarchyService(),
          new FakeCentralPackageVersionService(centralPackages));

  private sealed class FakeHierarchyService : IProjXWorkspaceHierarchyService
  {
    public Task<ProjXWorkspaceHierarchy> ResolveAsync(string projectPath, CancellationToken cancellationToken) =>
        Task.FromResult(new ProjXWorkspaceHierarchy(
            projectPath,
            WorkspaceRoot: "/repo",
            DirectoryBuildPropsPath: null,
            DirectoryBuildTargetsPath: null,
            ManagePackageVersionsCentrally: true,
            DirectoryPackagesPropsPath: "/repo/Directory.Packages.props"));
  }

  private sealed class FakeCentralPackageVersionService(IReadOnlyList<CentralPackageVersionInfo> packages) : ICentralPackageVersionService
  {
    public Task<IReadOnlyList<CentralPackageVersionInfo>> GetPackageVersionsAsync(string projectPath, CancellationToken cancellationToken) =>
        Task.FromResult(packages);
  }
}
