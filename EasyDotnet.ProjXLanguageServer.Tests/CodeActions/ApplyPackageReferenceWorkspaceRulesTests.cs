using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Tests.CodeActions;

public sealed class ApplyPackageReferenceWorkspaceRulesTests
{
  private const string ProjectPath = "/repo/src/App/App.csproj";
  private const string CentralPackagePath = "/repo/Directory.Packages.props";

  [Test]
  public async Task PackageReferenceWithVersion_OffersWorkspaceRulesAction_AndMovesVersionToCentralProps()
  {
    const string project =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";
    const string central =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageVersion Include=\"Serilog\" Version=\"3.1.1\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    var sut = CreateSut(project, central);
    var actions = await sut.GetCodeActionsAsync(Docs.Make(project, ProjectPath), WholeDocument(project), [], CancellationToken.None);

    var action = actions.Single(a => a.Title == "Apply PackageReference using workspace rules");
    var projectResult = ApplyEdits(project, action.Edit!.Changes![UriFor(ProjectPath)]);
    var centralResult = ApplyEdits(central, action.Edit!.Changes![UriFor(CentralPackagePath)]);

    await Assert.That(projectResult).Contains("<PackageReference Include=\"Newtonsoft.Json\" />");
    await Assert.That(projectResult).DoesNotContain("Version=\"13.0.3\"");
    await Assert.That(centralResult).Contains("<PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />");
  }

  private static CodeActionService CreateSut(string projectText, string centralPackageText)
  {
    var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
    {
      [ProjectPath] = new(projectText),
      [CentralPackagePath] = new(centralPackageText),
    });
    var documentManager = new DocumentManager();
    var planner = new PackageReferenceEditPlanner(
        new FakeHierarchyService(new ProjXWorkspaceHierarchy(
            ProjectPath,
            WorkspaceRoot: "/repo",
            DirectoryBuildPropsPath: null,
            DirectoryBuildTargetsPath: null,
            ManagePackageVersionsCentrally: true,
            DirectoryPackagesPropsPath: CentralPackagePath)),
        new ProjXDocumentTextProvider(documentManager, fileSystem));

    return new CodeActionService(new UserSecretsResolver(fileSystem), planner);
  }

  private static LspRange WholeDocument(string text)
  {
    var lines = text.Split('\n');
    return new LspRange
    {
      Start = new Position { Line = 0, Character = 0 },
      End = new Position { Line = lines.Length - 1, Character = lines[^1].Length }
    };
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
    public Task<ProjXWorkspaceHierarchy> ResolveAsync(string projectPath, CancellationToken cancellationToken) =>
        Task.FromResult(hierarchy);
  }
}