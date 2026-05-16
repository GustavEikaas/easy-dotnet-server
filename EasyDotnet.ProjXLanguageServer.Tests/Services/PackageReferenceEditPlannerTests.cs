using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Services;

public sealed class PackageReferenceEditPlannerTests
{
  private const string ProjectPath = "/repo/src/App/App.csproj";
  private const string CentralPackagePath = "/repo/Directory.Packages.props";

  [Test]
  public async Task AddPackageReference_WithoutCentralPackageManagement_AddsVersionToProject()
  {
    const string project =
        "<Project>\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net8.0</TargetFramework>\n" +
        "  </PropertyGroup>\n" +
        "</Project>\n";

    var sut = CreateSut(project, centralPackageText: null, manageCentrally: false);

    var edit = sut.PlanAddPackageReference(new AddPackageReferenceEditRequest(ProjectPath, "Newtonsoft.Json", "13.0.3"));
    var result = ApplyEdits(project, edit.Changes![UriFor(ProjectPath)]);

    await Assert.That(edit.Changes!.Keys).IsEquivalentTo([UriFor(ProjectPath)]);
    await Assert.That(result).Contains("<PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />");
  }

  [Test]
  public async Task AddPackageReference_WithCentralPackageManagement_SplitsProjectAndCentralVersionEdits()
  {
    const string project =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <Compile Include=\"Program.cs\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";
    const string central =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Include=\"Serilog\" Version=\"3.1.1\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    var sut = CreateSut(project, central, manageCentrally: true);

    var edit = sut.PlanAddPackageReference(new AddPackageReferenceEditRequest(ProjectPath, "Newtonsoft.Json", "13.0.3"));
    var projectResult = ApplyEdits(project, edit.Changes![UriFor(ProjectPath)]);
    var centralResult = ApplyEdits(central, edit.Changes![UriFor(CentralPackagePath)]);

    await Assert.That(edit.Changes!.Keys).IsEquivalentTo([UriFor(ProjectPath), UriFor(CentralPackagePath)]);
    await Assert.That(projectResult).Contains("<PackageReference Include=\"Newtonsoft.Json\" />");
    await Assert.That(projectResult).DoesNotContain("<PackageReference Include=\"Newtonsoft.Json\" Version=");
    await Assert.That(centralResult).Contains("<PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />");
  }

  [Test]
  public async Task AddPackageReference_WithExistingCentralVersion_UpdatesVersionInsteadOfDuplicating()
  {
    const string project =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"12.0.1\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";
    const string central =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Update=\"Newtonsoft.Json\" Version=\"12.0.1\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    var sut = CreateSut(project, central, manageCentrally: true);

    var edit = sut.PlanAddPackageReference(new AddPackageReferenceEditRequest(ProjectPath, "Newtonsoft.Json", "13.0.3"));
    var projectResult = ApplyEdits(project, edit.Changes![UriFor(ProjectPath)]);
    var centralResult = ApplyEdits(central, edit.Changes![UriFor(CentralPackagePath)]);

    await Assert.That(projectResult).Contains("<PackageReference Include=\"Newtonsoft.Json\" />");
    await Assert.That(projectResult).DoesNotContain("Version=\"12.0.1\"");
    await Assert.That(centralResult).Contains("<PackageVersion Update=\"Newtonsoft.Json\" Version=\"13.0.3\" />");
    await Assert.That(centralResult.IndexOf("Newtonsoft.Json", StringComparison.Ordinal)).IsEqualTo(
        centralResult.LastIndexOf("Newtonsoft.Json", StringComparison.Ordinal));
  }

  [Test]
  public async Task AddPackageReference_WithCentralPackageManagement_AddsVersionToFirstPackageVersionItemGroup()
  {
    const string project = "<Project>\n</Project>\n";
    const string central =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <Compile Include=\"Shared.cs\" />\n" +
        "    <PackageVersion Include=\"Serilog\" Version=\"3.1.1\" />\n" +
        "  </ItemGroup>\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Include=\"Dapper\" Version=\"2.1.66\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    var sut = CreateSut(project, central, manageCentrally: true);

    var edit = sut.PlanAddPackageReference(new AddPackageReferenceEditRequest(ProjectPath, "Newtonsoft.Json", "13.0.3"));
    var centralResult = ApplyEdits(central, edit.Changes![UriFor(CentralPackagePath)]);

    await Assert.That(centralResult.IndexOf("Newtonsoft.Json", StringComparison.Ordinal)).IsLessThan(
        centralResult.IndexOf("Dapper", StringComparison.Ordinal));
  }

  [Test]
  public async Task AddPackageReference_UsesOpenCentralPackageBufferWhenAvailable()
  {
    const string project = "<Project>\n</Project>\n";
    const string diskCentral =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Include=\"DiskOnly\" Version=\"1.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";
    const string openCentral =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Include=\"OpenOnly\" Version=\"1.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    var documentManager = new DocumentManager();
    documentManager.OpenDocument(new Uri(CentralPackagePath), openCentral, version: 2);
    var sut = CreateSut(project, diskCentral, manageCentrally: true, documentManager);

    var edit = sut.PlanAddPackageReference(new AddPackageReferenceEditRequest(ProjectPath, "Newtonsoft.Json", "13.0.3"));
    var centralResult = ApplyEdits(openCentral, edit.Changes![UriFor(CentralPackagePath)]);

    await Assert.That(centralResult).Contains("OpenOnly");
    await Assert.That(centralResult).DoesNotContain("DiskOnly");
    await Assert.That(centralResult).Contains("<PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />");
  }

  private static PackageReferenceEditPlanner CreateSut(
      string projectText,
      string? centralPackageText,
      bool manageCentrally,
      DocumentManager? documentManager = null)
  {
    var files = new Dictionary<string, MockFileData>
    {
      [ProjectPath] = new(projectText),
    };
    if (centralPackageText is not null)
    {
      files[CentralPackagePath] = new MockFileData(centralPackageText);
    }

    var fileSystem = new MockFileSystem(files);
    documentManager ??= new DocumentManager();
    var textProvider = new ProjXDocumentTextProvider(documentManager, fileSystem);
    var hierarchy = new ProjXWorkspaceHierarchy(
        ProjectPath,
        WorkspaceRoot: "/repo",
        DirectoryBuildPropsPath: null,
        DirectoryBuildTargetsPath: null,
        ManagePackageVersionsCentrally: manageCentrally,
        DirectoryPackagesPropsPath: centralPackageText is null ? null : CentralPackagePath);
    return new PackageReferenceEditPlanner(new FakeHierarchyService(hierarchy), textProvider);
  }

  private static string ApplyEdits(string original, TextEdit[] edits)
  {
    var doc = Docs.Make(original);
    var ordered = edits
        .Select(e => (
            start: doc.ToOffset(e.Range.Start.Line, e.Range.Start.Character),
            end: doc.ToOffset(e.Range.End.Line, e.Range.End.Character),
            text: e.NewText))
        .OrderByDescending(e => e.start)
        .ToList();

    var result = original;
    foreach (var (start, end, text) in ordered)
    {
      result = string.Concat(result.AsSpan(0, start), text, result.AsSpan(end));
    }

    return result;
  }

  private static string UriFor(string path) => new Uri(path).ToString();

  private sealed class FakeHierarchyService(ProjXWorkspaceHierarchy hierarchy) : IProjXWorkspaceHierarchyService
  {
    public ProjXWorkspaceHierarchy Resolve(string projectPath) => hierarchy;
  }
}
