using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace EasyDotnet.RoslynLanguageServices.CodeActions;

public static class MissingTypeDiagnostics
{
  public const string CS0246 = nameof(CS0246); // The type or namespace name '{0}' could not be found (are you missing a using directive or an assembly reference?)
  public const string CS0103 = nameof(CS0103); // The name '{0}' does not exist in the current context
}

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImportAllNamespacesCodeFixProvider)), Shared]
public class ImportAllNamespacesCodeFixProvider : CodeFixProvider
{
  private const string Title = "Import all missing namespaces";

  public sealed override ImmutableArray<string> FixableDiagnosticIds => [MissingTypeDiagnostics.CS0246, MissingTypeDiagnostics.CS0103];

  public sealed override FixAllProvider GetFixAllProvider() => null!;

  public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
  {
    var diagnostic = context.Diagnostics[0];

    var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
    if (semanticModel == null)
      return;

    var allDiagnostics = semanticModel.GetDiagnostics();
    var missingTypeDiagnostics = allDiagnostics.Where(d => FixableDiagnosticIds.Contains(d.Id)).ToList();

    if (missingTypeDiagnostics.Count <= 1)
    {
      return;
    }

    foreach (var diag in missingTypeDiagnostics)
    {
      context.RegisterCodeFix(
          CodeAction.Create(
              title: Title,
              createChangedDocument: c => ImportAllMissingNamespacesAsync(context.Document, missingTypeDiagnostics, c),
              equivalenceKey: Title),
          diag);
    }
  }

  private static async Task<Document> ImportAllMissingNamespacesAsync(Document document, List<Diagnostic> missingTypeDiagnostics, CancellationToken cancellationToken)
  {
    var compilation = await document.Project.GetCompilationAsync(cancellationToken);
    if (compilation == null)
    {
      return document;
    }

    var root = await document.GetSyntaxRootAsync(cancellationToken);
    if (root == null)
    {
      return document;
    }

    var missingTypes = missingTypeDiagnostics
        .Select(d => GetIdentifierFromDiagnostic(root, d))
        .Where(id => id != null)
        .Distinct()
        .ToList();

    var namespacesToAdd = new HashSet<string>();

    // For each missing type, find potential namespaces
    foreach (var typeName in missingTypes)
    {
      var namespacesForType = FindNamespacesForType(compilation, typeName!);
      var isAmbigiousType = namespacesForType.Count > 1;

      if (isAmbigiousType || namespacesForType.Count == 0)
      {
        continue;
      }

      namespacesToAdd.Add(namespacesForType[0]);
    }

    if (root is not CompilationUnitSyntax compilationUnit)
    {
      return document;
    }

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
    {
      return identifier.Identifier.Text;
    }

    return node is GenericNameSyntax genericName ? genericName.Identifier.Text : null;
  }

  private static List<string> FindNamespacesForType(Compilation compilation, string typeName)
  {
    var namespaces = new List<string>();

    FindTypeInNamespace(compilation.Assembly.GlobalNamespace, typeName, namespaces);

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