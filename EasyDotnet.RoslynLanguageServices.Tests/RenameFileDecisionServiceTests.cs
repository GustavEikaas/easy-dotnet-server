using EasyDotnet.RoslynLanguageServices.Rename;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace EasyDotnet.RoslynLanguageServices.Tests;

public sealed class RenameFileDecisionServiceTests
{
  [Test]
  public async Task MatchingPrimaryType_ReturnsRename()
  {
    var document = CreateDocument("/tmp/Customer.cs", "namespace App;\npublic class Customer { }\n");

    var response = await Decide(document, "Customer", "Client");

    await Assert.That(response.ShouldRename).IsTrue();
    await Assert.That(response.OldUri).EndsWith("/Customer.cs");
    await Assert.That(response.NewUri).EndsWith("/Client.cs");
  }

  [Test]
  public async Task RenameFromReference_RenamesDeclaringFile()
  {
    using var workspace = new AdhocWorkspace();
    var project = CreateProject(workspace);
    project = project.AddDocument("Customer.cs", SourceText.From("namespace App;\npublic class Customer { }\n"), filePath: "/tmp/Customer.cs").Project;
    var referenceDocument = project.AddDocument("Use.cs", SourceText.From("namespace App;\npublic class Use { Customer Field; }\n"), filePath: "/tmp/Use.cs");

    var response = await Decide(referenceDocument, "Customer", "Client");

    await Assert.That(response.ShouldRename).IsTrue();
    await Assert.That(response.OldUri).EndsWith("/Customer.cs");
    await Assert.That(response.NewUri).EndsWith("/Client.cs");
  }

  [Test]
  public async Task MethodRename_IsIgnored()
  {
    var document = CreateDocument("/tmp/Customer.cs", "public class Customer { void Save() { } }\n");

    var response = await Decide(document, "Save", "Store");

    await Assert.That(response.ShouldRename).IsFalse();
  }

  [Test]
  public async Task PartialType_IsIgnored()
  {
    using var workspace = new AdhocWorkspace();
    var project = CreateProject(workspace);
    project = project.AddDocument("Customer.cs", SourceText.From("public partial class Customer { }\n"), filePath: "/tmp/Customer.cs").Project;
    var document = project.AddDocument("Customer.Partial.cs", SourceText.From("public partial class Customer { }\n"), filePath: "/tmp/Customer.Partial.cs");

    var response = await Decide(document, "Customer", "Client");

    await Assert.That(response.ShouldRename).IsFalse();
  }

  [Test]
  public async Task FileNameMatchingTypeInMultiTypeFile_ReturnsRename()
  {
    var document = CreateDocument("/tmp/Customer.cs", "public class Customer { }\npublic class Other { }\n");

    var response = await Decide(document, "Customer", "Client");

    await Assert.That(response.ShouldRename).IsTrue();
    await Assert.That(response.OldUri).EndsWith("/Customer.cs");
    await Assert.That(response.NewUri).EndsWith("/Client.cs");
  }

  [Test]
  public async Task FileNameMatchingInterfaceInMultiTypeFile_ReturnsRename()
  {
    var document = CreateDocument("/tmp/ICustomer.cs", "public interface ICustomer { }\npublic class Customer { }\n");

    var response = await Decide(document, "ICustomer", "IClient");

    await Assert.That(response.ShouldRename).IsTrue();
    await Assert.That(response.OldUri).EndsWith("/ICustomer.cs");
    await Assert.That(response.NewUri).EndsWith("/IClient.cs");
  }

  [Test]
  public async Task FileNameMatchingRecordInMultiTypeFile_ReturnsRename()
  {
    var document = CreateDocument("/tmp/Customer.cs", "public record Customer;\npublic class CustomerValidator { }\n");

    var response = await Decide(document, "Customer", "Client");

    await Assert.That(response.ShouldRename).IsTrue();
    await Assert.That(response.OldUri).EndsWith("/Customer.cs");
    await Assert.That(response.NewUri).EndsWith("/Client.cs");
  }

  [Test]
  public async Task NonFileNameMatchingTypeInMultiTypeFile_IsIgnored()
  {
    var document = CreateDocument("/tmp/Customer.cs", "public class Customer { }\npublic class Other { }\n");

    var response = await Decide(document, "Other", "Else");

    await Assert.That(response.ShouldRename).IsFalse();
  }

  [Test]
  public async Task NestedType_IsIgnored()
  {
    var document = CreateDocument("/tmp/Outer.cs", "public class Outer { public class Inner { } }\n");

    var response = await Decide(document, "Inner", "Nested");

    await Assert.That(response.ShouldRename).IsFalse();
  }

  private static async Task<ShouldRenameFileResponse> Decide(Document document, string tokenText, string newName)
  {
    var text = await document.GetTextAsync();
    var source = text.ToString();
    var index = source.IndexOf(tokenText, StringComparison.Ordinal);
    if (index < 0)
    {
      throw new InvalidOperationException($"Could not find token '{tokenText}'.");
    }

    var line = text.Lines.GetLineFromPosition(index);
    return await RenameFileDecisionService.ShouldRenameFileAsync(
        document,
        line.LineNumber,
        index - line.Start,
        newName,
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
