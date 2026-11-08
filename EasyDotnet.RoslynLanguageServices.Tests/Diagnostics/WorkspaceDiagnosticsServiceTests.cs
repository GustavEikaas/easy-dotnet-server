using EasyDotnet.RoslynLanguageServices.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace EasyDotnet.RoslynLanguageServices.Tests.Diagnostics;

public sealed class WorkspaceDiagnosticsServiceTests
{
  private const string BrokenSource = "namespace App;\npublic class Broken\n{\n  public int Value => Missing;\n}\n";
  private const string WarningSource = "namespace App;\npublic class Warn\n{\n  public void M() { int unused; }\n}\n";

  [Test]
  public async Task CompilerError_IsReportedWithLocation()
  {
    using var workspace = new AdhocWorkspace();
    var solution = AddProject(workspace.CurrentSolution, "App", "/src/App/App.csproj", ("/src/App/Broken.cs", BrokenSource));

    var response = await WorkspaceDiagnosticsService.GetDiagnosticsAsync(solution, null, false, CancellationToken.None);

    var diagnostic = await Assert.That(response.Diagnostics).HasSingleItem();
    await Assert.That(diagnostic!.Code).IsEqualTo("CS0103");
    await Assert.That(diagnostic.FilePath).IsEqualTo("/src/App/Broken.cs");
    await Assert.That(diagnostic.Severity).IsEqualTo(1);
    await Assert.That(diagnostic.Project).IsEqualTo("App");
    await Assert.That(diagnostic.Range.Start).IsEqualTo(new WorkspaceDiagnosticPosition(3, 22));
    await Assert.That(response.ProjectCount).IsEqualTo(1);
  }

  [Test]
  public async Task Warnings_AreExcludedUnlessRequested()
  {
    using var workspace = new AdhocWorkspace();
    var solution = AddProject(workspace.CurrentSolution, "App", "/src/App/App.csproj", ("/src/App/Warn.cs", WarningSource));

    var errorsOnly = await WorkspaceDiagnosticsService.GetDiagnosticsAsync(solution, null, false, CancellationToken.None);
    var withWarnings = await WorkspaceDiagnosticsService.GetDiagnosticsAsync(solution, null, true, CancellationToken.None);

    await Assert.That(errorsOnly.Diagnostics).IsEmpty();
    var warning = await Assert.That(withWarnings.Diagnostics).HasSingleItem();
    await Assert.That(warning!.Code).IsEqualTo("CS0168");
    await Assert.That(warning.Severity).IsEqualTo(2);
  }

  [Test]
  public async Task PragmaSuppressedWarning_IsExcluded()
  {
    using var workspace = new AdhocWorkspace();
    var source = "#pragma warning disable CS0168\n" + WarningSource;
    var solution = AddProject(workspace.CurrentSolution, "App", "/src/App/App.csproj", ("/src/App/Warn.cs", source));

    var response = await WorkspaceDiagnosticsService.GetDiagnosticsAsync(solution, null, true, CancellationToken.None);

    await Assert.That(response.Diagnostics).IsEmpty();
  }

  [Test]
  public async Task ProjectTarget_OnlyAnalyzesThatProject()
  {
    using var workspace = new AdhocWorkspace();
    var solution = AddProject(workspace.CurrentSolution, "App", "/src/App/App.csproj", ("/src/App/Broken.cs", BrokenSource));
    solution = AddProject(solution, "Lib", "/src/Lib/Lib.csproj", ("/src/Lib/Broken.cs", BrokenSource));

    var response = await WorkspaceDiagnosticsService.GetDiagnosticsAsync(solution, "/src/Lib/Lib.csproj", false, CancellationToken.None);

    var diagnostic = await Assert.That(response.Diagnostics).HasSingleItem();
    await Assert.That(diagnostic!.FilePath).IsEqualTo("/src/Lib/Broken.cs");
    await Assert.That(response.ProjectCount).IsEqualTo(1);
  }

  [Test]
  public async Task SolutionTarget_AnalyzesAllProjects()
  {
    using var workspace = new AdhocWorkspace();
    var solution = AddProject(workspace.CurrentSolution, "App", "/src/App/App.csproj", ("/src/App/Broken.cs", BrokenSource));
    solution = AddProject(solution, "Lib", "/src/Lib/Lib.csproj", ("/src/Lib/Broken.cs", BrokenSource));

    var response = await WorkspaceDiagnosticsService.GetDiagnosticsAsync(solution, "/src/App.sln", false, CancellationToken.None);

    await Assert.That(response.Diagnostics.Select(static d => d.FilePath)).IsEquivalentTo(["/src/App/Broken.cs", "/src/Lib/Broken.cs"]);
    await Assert.That(response.ProjectCount).IsEqualTo(2);
  }

  [Test]
  public async Task UnknownProjectTarget_ReportsNoProjects()
  {
    using var workspace = new AdhocWorkspace();
    var solution = AddProject(workspace.CurrentSolution, "App", "/src/App/App.csproj", ("/src/App/Broken.cs", BrokenSource));

    var response = await WorkspaceDiagnosticsService.GetDiagnosticsAsync(solution, "/src/Other/Other.csproj", false, CancellationToken.None);

    await Assert.That(response.ProjectCount).IsEqualTo(0);
    await Assert.That(response.Diagnostics).IsEmpty();
  }

  [Test]
  public async Task MultiTargetedProject_ReportsSharedFileOnce()
  {
    using var workspace = new AdhocWorkspace();
    var solution = AddProject(workspace.CurrentSolution, "App(net8.0)", "/src/App/App.csproj", ("/src/App/Broken.cs", BrokenSource));
    solution = AddProject(solution, "App(net9.0)", "/src/App/App.csproj", ("/src/App/Broken.cs", BrokenSource));

    var response = await WorkspaceDiagnosticsService.GetDiagnosticsAsync(solution, "/src/App/App.csproj", false, CancellationToken.None);

    await Assert.That(response.Diagnostics).HasSingleItem();
    await Assert.That(response.ProjectCount).IsEqualTo(2);
  }

  private static Solution AddProject(Solution solution, string name, string filePath, params (string Path, string Source)[] documents)
  {
    var projectId = ProjectId.CreateNewId();
    solution = solution.AddProject(ProjectInfo.Create(
        projectId,
        VersionStamp.Create(),
        name,
        name,
        LanguageNames.CSharp,
        filePath: filePath,
        compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
        parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
        metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));

    foreach (var (path, source) in documents)
    {
      solution = solution.AddDocument(DocumentId.CreateNewId(projectId), Path.GetFileName(path), SourceText.From(source), filePath: path);
    }

    return solution;
  }
}