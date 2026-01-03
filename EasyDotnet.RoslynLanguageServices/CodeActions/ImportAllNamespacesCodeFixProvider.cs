using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace EasyDotnet.RoslynLanguageServices.CodeActions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImportAllNamespacesCodeFixProvider)), Shared]
public class ImportAllNamespacesCodeFixProvider : CodeFixProvider
{
  private const string Title = "Import all missing namespaces";

  public sealed override ImmutableArray<string> FixableDiagnosticIds =>
      ["CS0246"]; // The type or namespace name could not be found

  public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

  public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
  {
    var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
    var diagnostic = context.Diagnostics[0];

    // Check if there are multiple CS0246 errors in the document
    var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
    if (semanticModel == null)
      return;

    var allDiagnostics = semanticModel.GetDiagnostics();
    var missingTypeDiagnostics = allDiagnostics.Where(d => d.Id == "CS0246").ToList();

    if (missingTypeDiagnostics.Count > 1)
    {
      context.RegisterCodeFix(
          CodeAction.Create(
              title: Title,
              createChangedDocument: c => ImportAllMissingNamespacesAsync(context.Document, c),
              equivalenceKey: Title),
          diagnostic);
    }
  }

  private async Task<Document> ImportAllMissingNamespacesAsync(Document document, CancellationToken cancellationToken)
  {
    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
    if (semanticModel == null)
      return document;

    var root = await document.GetSyntaxRootAsync(cancellationToken);
    if (root == null)
      return document;

    var compilation = await document.Project.GetCompilationAsync(cancellationToken);
    if (compilation == null)
      return document;

    // Find all CS0246 diagnostics
    var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);
    var missingTypes = diagnostics
        .Where(d => d.Id == "CS0246")
        .Select(d => GetIdentifierFromDiagnostic(root, d))
        .Where(id => id != null)
        .Distinct()
        .ToList();

    var namespacesToAdd = new HashSet<string>();

    // For each missing type, find potential namespaces
    foreach (var typeName in missingTypes)
    {
      foreach (var ns in FindNamespacesForType(compilation, typeName!))
      {
        namespacesToAdd.Add(ns);
      }
    }

    if (root is not CompilationUnitSyntax compilationUnit)
      return document;

    var newRoot = compilationUnit;
    foreach (var ns in namespacesToAdd.Order())
    {
      // Check if using already exists
      if (!compilationUnit.Usings.Any(u => u.Name?.ToString() == ns))
      {
        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns))
            .WithAdditionalAnnotations(Formatter.Annotation);
        newRoot = newRoot.AddUsings(usingDirective);
      }
    }

    return document.WithSyntaxRoot(newRoot);
  }

  private static string? GetIdentifierFromDiagnostic(SyntaxNode root, Diagnostic diagnostic)
  {
    var span = diagnostic.Location.SourceSpan;
    var node = root.FindNode(span);

    if (node is IdentifierNameSyntax identifier)
      return identifier.Identifier.Text;

    return node is GenericNameSyntax genericName ? genericName.Identifier.Text : null;
  }

  private static List<string> FindNamespacesForType(Compilation compilation, string typeName)
  {
    var namespaces = new List<string>();

    foreach (var reference in compilation.References)
    {
      if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
      {
        FindTypeInNamespace(assembly.GlobalNamespace, typeName, namespaces);
      }
    }

    return namespaces;
  }

  private static void FindTypeInNamespace(INamespaceSymbol namespaceSymbol, string typeName, List<string> namespaces)
  {
    // Check if this namespace contains the type
    var typeSymbol = namespaceSymbol.GetTypeMembers(typeName).FirstOrDefault();
    if (typeSymbol != null && typeSymbol.DeclaredAccessibility == Accessibility.Public)
    {
      var fullNamespace = namespaceSymbol.ToDisplayString();
      if (!string.IsNullOrEmpty(fullNamespace))
      {
        namespaces.Add(fullNamespace);
      }
    }

    // Recursively search child namespaces
    foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
    {
      FindTypeInNamespace(childNamespace, typeName, namespaces);
    }
  }
}