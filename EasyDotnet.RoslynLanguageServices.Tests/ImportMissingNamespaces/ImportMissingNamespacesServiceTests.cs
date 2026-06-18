using System.Text.Json;
using EasyDotnet.RoslynLanguageServices.ImportMissingNamespaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace EasyDotnet.RoslynLanguageServices.Tests.ImportMissingNamespaces;

public sealed class ImportMissingNamespacesServiceTests
{
  [Test]
  public async Task UnresolvedType_ResolvesSingleNamespace()
  {
    var document = CreateDocument("/tmp/Use.cs", "public class Use { StringBuilder Value; }\n");

    var response = await ImportMissingNamespacesService.ImportMissingNamespacesAsync(document, CancellationToken.None);

    await Assert.That(response.CanImport).IsTrue();
    await Assert.That(response.Usings).IsEquivalentTo(new[] { "using System.Text;" });
  }

  [Test]
  public async Task ExtensionMethod_ResolvesNamespace()
  {
    var document = CreateDocument(
        "/tmp/Use.cs",
        "public class Use { void M() { new System.Collections.Generic.List<int>().Where(x => x > 0); } }\n");

    var response = await ImportMissingNamespacesService.ImportMissingNamespacesAsync(document, CancellationToken.None);

    await Assert.That(response.CanImport).IsTrue();
    await Assert.That(response.Usings).Contains("using System.Linq;");
  }

  [Test]
  public async Task AmbiguousType_IsSkipped()
  {
    // Timer exists in both System.Threading and System.Timers.
    var document = CreateDocument("/tmp/Use.cs", "public class Use { Timer Value; }\n");

    var response = await ImportMissingNamespacesService.ImportMissingNamespacesAsync(document, CancellationToken.None);

    await Assert.That(response.CanImport).IsFalse();
  }

  [Test]
  public async Task MultipleTypesInSameNamespace_AreDeduplicated()
  {
    var document = CreateDocument("/tmp/Use.cs", "public class Use { StringBuilder A; ASCIIEncoding B; }\n");

    var response = await ImportMissingNamespacesService.ImportMissingNamespacesAsync(document, CancellationToken.None);

    await Assert.That(response.CanImport).IsTrue();
    await Assert.That(response.Usings).IsEquivalentTo(new[] { "using System.Text;" });
  }

  [Test]
  public async Task AlreadyImportedNamespace_IsNotReturned()
  {
    var document = CreateDocument("/tmp/Use.cs", "using System.Text;\npublic class Use { StringBuilder Value; }\n");

    var response = await ImportMissingNamespacesService.ImportMissingNamespacesAsync(document, CancellationToken.None);

    await Assert.That(response.CanImport).IsFalse();
  }

  [Test]
  public async Task NothingMissing_ReturnsNo()
  {
    var document = CreateDocument("/tmp/Use.cs", "public class Use { int Value; }\n");

    var response = await ImportMissingNamespacesService.ImportMissingNamespacesAsync(document, CancellationToken.None);

    await Assert.That(response.CanImport).IsFalse();
  }

  [Test]
  public async Task Response_SerializesAsCamelCaseForLuaClient()
  {
    var response = ImportMissingNamespacesResponse.Yes(["using System.Text;"]);

    var json = JsonSerializer.Serialize(response);

    await Assert.That(json).Contains("\"canImport\":true");
    await Assert.That(json).Contains("\"usings\":[\"using System.Text;\"]");
  }

  private static Document CreateDocument(string filePath, string source)
  {
    var workspace = new AdhocWorkspace();
    return CreateProject(workspace)
        .AddDocument(Path.GetFileName(filePath), SourceText.From(source), filePath: filePath);
  }

  private static Project CreateProject(AdhocWorkspace workspace)
  {
    var projectInfo = ProjectInfo.Create(
        ProjectId.CreateNewId(),
        VersionStamp.Create(),
        "TestProject",
        "TestProject",
        LanguageNames.CSharp,
        parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
        metadataReferences: GetPlatformReferences());

    return workspace.AddProject(projectInfo);
  }

  private static IEnumerable<MetadataReference> GetPlatformReferences()
    => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => !string.IsNullOrEmpty(path))
        .Select(path => MetadataReference.CreateFromFile(path));
}