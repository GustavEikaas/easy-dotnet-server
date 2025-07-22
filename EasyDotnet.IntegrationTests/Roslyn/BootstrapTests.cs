using System.Runtime.InteropServices;
using EasyDotnet.Controllers.Roslyn;
using EasyDotnet.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.IntegrationTests.Roslyn;

public class BootstrapTests
{
  private readonly string _validLineFeed = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\r\n" : "\n";
  private readonly string _invalidLineFeed = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\n" : "\r\n";

  [Theory]
  [InlineData(Kind.Record, false)]
  [InlineData(Kind.Record, true)]
  [InlineData(Kind.Class, false)]
  [InlineData(Kind.Class, true)]
  [InlineData(Kind.Interface, false)]
  [InlineData(Kind.Interface, true)]
  public async Task BootstrapCreatesCorrectKind(Kind kind, bool preferFileScopedNamespace)
  {
    using var res = await GetSyntaxTreeForBootstrappedFile(kind == Kind.Interface ? "IMyController" : "MyController", kind, preferFileScopedNamespace);

    BaseNamespaceDeclarationSyntax? ns = preferFileScopedNamespace ? await GetFileScopedNamespaceNode(res.SyntaxTree) : await GetNamespaceNode(res.SyntaxTree);
    Assert.NotNull(ns);

    switch (kind)
    {
      case Kind.Record:
        var recordDecl = await GetNode<RecordDeclarationSyntax>(res.SyntaxTree);
        Assert.NotNull(recordDecl);
        Assert.Equal("MyController", recordDecl.Identifier.Text);
        break;

      case Kind.Class:
        var classDecl = await GetNode<ClassDeclarationSyntax>(res.SyntaxTree);
        Assert.NotNull(classDecl);
        Assert.Equal("MyController", classDecl.Identifier.Text);
        break;

      case Kind.Interface:
        var interfaceDecl = await GetNode<InterfaceDeclarationSyntax>(res.SyntaxTree);
        Assert.NotNull(interfaceDecl);
        Assert.Equal("IMyController", interfaceDecl.Identifier.Text);
        break;

      default:
        Assert.Fail($"Unsupported kind: {kind}");
        break;
    }
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task BootstrapNamespace(bool preferFileScopedNamespace)
  {
    using var res = await GetSyntaxTreeForBootstrappedFile("IMyController", Kind.Interface, preferFileScopedNamespace);
    BaseNamespaceDeclarationSyntax? ns = preferFileScopedNamespace ? await GetFileScopedNamespaceNode(res.SyntaxTree) : await GetNamespaceNode(res.SyntaxTree);
    Assert.NotNull(ns);

    var expectedNamespace = "MyTempApp.Controllers";
    Assert.Equal(expectedNamespace, ns.Name.ToString());
  }

  [Fact]
  public async Task BootstrapNamespaceWithCorrectLineFeeds()
  {
    using var res = await GetSyntaxTreeForBootstrappedFile("MyController", Kind.Record, true);

    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      Assert.DoesNotContain(_invalidLineFeed, res.RawText);
    }
    Assert.Contains(_validLineFeed, res.RawText);
  }

  private static async Task<NamespaceDeclarationSyntax?> GetNamespaceNode(SyntaxTree syntaxTree)
  {
    var root = await syntaxTree.GetRootAsync();
    var namespaceDecl = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().SingleOrDefault();
    return namespaceDecl;
  }

  private static async Task<FileScopedNamespaceDeclarationSyntax?> GetFileScopedNamespaceNode(SyntaxTree syntaxTree)
  {
    var root = await syntaxTree.GetRootAsync();
    var namespaceDecl = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().SingleOrDefault();
    return namespaceDecl;
  }

  private static async Task<T?> GetNode<T>(SyntaxTree syntaxTree) where T : SyntaxNode
  {
    var root = await syntaxTree.GetRootAsync();
    return root.DescendantNodes().OfType<T>().SingleOrDefault();
  }

  private static (TempDotNetProject Project, string ControllerFilePath) CreateDummyProjectWithFreshFile(string filename)
  {
    var tempProject = new TempDotNetProject("MyTempApp");

    var controllersDir = Path.Combine(tempProject.ProjectDirectory, "Controllers");
    Directory.CreateDirectory(controllersDir);

    var controllerFilePath = Path.Combine(controllersDir, $"{filename}.cs");
    File.Create(controllerFilePath).Dispose();

    return (tempProject, controllerFilePath);
  }

  private async Task<BootstrappedFileResult> GetSyntaxTreeForBootstrappedFile(
      string controllerName,
      Kind kind,
      bool preferFileScopedNamespace)
  {
    var (project, controllerFilePath) = CreateDummyProjectWithFreshFile(controllerName);

    var roslynService = new RoslynService(new RoslynProjectMetadataCache());
    await roslynService.BootstrapFile(
        controllerFilePath,
        kind,
        preferFileScopedNamespace: preferFileScopedNamespace,
        cancellationToken: CancellationToken.None);

    var code = await File.ReadAllTextAsync(controllerFilePath);
    var syntaxTree = CSharpSyntaxTree.ParseText(code);

    return new BootstrappedFileResult(project, controllerFilePath, syntaxTree, code);
  }

  private record BootstrappedFileResult(
      TempDotNetProject Project,
      string ControllerFilePath,
      SyntaxTree SyntaxTree,
      string RawText) : IDisposable
  {
    public void Dispose() => Project.Dispose();
  }
}