using EasyDotnet.RoslynLanguageServices.CreateType;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace EasyDotnet.RoslynLanguageServices.Tests.CreateType;

public sealed class CreateTypeFromUsageServiceTests
{
  [Test]
  public async Task UnresolvedVariableType_ReturnsCreateClassPlan()
  {
    var document = CreateDocument("/tmp/Use.cs", "namespace App;\npublic class Use { Customer Value; }\n");

    var response = await Decide(document, "Customer");

    await Assert.That(response.CanCreate).IsTrue();
    await Assert.That(response.TypeName).IsEqualTo("Customer");
    await Assert.That(response.FilePath).EndsWith("/Customer.cs");
    await Assert.That(response.FileText).IsEqualTo("namespace App;\n\npublic class Customer\n{\n}\n");
  }

  [Test]
  public async Task ExistingType_IsIgnored()
  {
    using var workspace = new AdhocWorkspace();
    var project = CreateProject(workspace);
    project = project.AddDocument("Customer.cs", SourceText.From("namespace App;\npublic class Customer { }\n"), filePath: "/tmp/Customer.cs").Project;
    var document = project.AddDocument("Use.cs", SourceText.From("namespace App;\npublic class Use { Customer Value; }\n"), filePath: "/tmp/Use.cs");

    var response = await Decide(document, "Customer");

    await Assert.That(response.CanCreate).IsFalse();
  }

  [Test]
  public async Task MemberAccess_IsIgnored()
  {
    var document = CreateDocument("/tmp/Use.cs", "public class Use { void M(dynamic value) { value.Customer(); } }\n");

    var response = await Decide(document, "Customer");

    await Assert.That(response.CanCreate).IsFalse();
  }

  [Test]
  public async Task BlockScopedNamespace_ReturnsFileScopedNamespace()
  {
    var document = CreateDocument("/tmp/Use.cs", "namespace App.Core { public class Use { Customer Value; } }\n");

    var response = await Decide(document, "Customer");

    await Assert.That(response.CanCreate).IsTrue();
    await Assert.That(response.FileText).IsEqualTo("namespace App.Core;\n\npublic class Customer\n{\n}\n");
  }

  [Test]
  public async Task UnresolvedGenericTypeArgument_ReturnsCreateClassPlan()
  {
    var document = CreateDocument("/tmp/Use.cs", "namespace App;\npublic class Use { System.Collections.Generic.List<Customer> Values; }\n");

    var response = await Decide(document, "Customer");

    await Assert.That(response.CanCreate).IsTrue();
    await Assert.That(response.TypeName).IsEqualTo("Customer");
  }

  private static async Task<CreateTypeFromUsageResponse> Decide(Document document, string tokenText)
  {
    var text = await document.GetTextAsync();
    var source = text.ToString();
    var index = source.IndexOf(tokenText, StringComparison.Ordinal);
    if (index < 0)
    {
      throw new InvalidOperationException($"Could not find token '{tokenText}'.");
    }

    var line = text.Lines.GetLineFromPosition(index);
    return await CreateTypeFromUsageService.CreateTypeFromUsageAsync(
        document,
        line.LineNumber,
        index - line.Start,
        CancellationToken.None);
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
        metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

    return workspace.AddProject(projectInfo);
  }
}
