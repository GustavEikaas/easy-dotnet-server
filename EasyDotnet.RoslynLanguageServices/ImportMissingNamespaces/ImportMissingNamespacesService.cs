using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace EasyDotnet.RoslynLanguageServices.ImportMissingNamespaces;

public static class ImportMissingNamespacesService
{
  private static readonly HashSet<string> TargetDiagnosticIds =
  [
    "CS0246", // type or namespace name could not be found
    "CS0234", // type or namespace name does not exist in the namespace
    "CS0103", // the name does not exist in the current context
    "CS1061", // no definition / no accessible extension method
  ];

  public static async Task<ImportMissingNamespacesResponse> ImportMissingNamespacesAsync(
      Document document,
      CancellationToken cancellationToken)
  {
    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
    if (root is null || semanticModel is null)
    {
      return ImportMissingNamespacesResponse.No("Could not resolve semantic model.");
    }

    var compilation = semanticModel.Compilation;
    var alreadyInScope = GetNamespacesInScope(root);
    var toImport = new SortedSet<string>(StringComparer.Ordinal);

    var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
        .Where(d => TargetDiagnosticIds.Contains(d.Id));

    foreach (var diagnostic in diagnostics)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

      var candidates = diagnostic.Id == "CS1061"
          ? await ResolveExtensionMethodNamespacesAsync(document.Project, semanticModel, compilation, node, cancellationToken).ConfigureAwait(false)
          : await ResolveTypeNamespacesAsync(document.Project, compilation, node, cancellationToken).ConfigureAwait(false);

      // Only auto-import when the symbol resolves to exactly one namespace; ambiguous
      // matches are skipped so we never guess on the user's behalf.
      var distinct = candidates.Where(ns => !alreadyInScope.Contains(ns)).Distinct().ToList();
      if (distinct.Count == 1)
      {
        toImport.Add(distinct[0]);
      }
    }

    if (toImport.Count == 0)
    {
      return ImportMissingNamespacesResponse.No("No missing namespaces could be resolved.");
    }

    var usings = toImport.Select(ns => $"using {ns};").ToArray();
    return ImportMissingNamespacesResponse.Yes(usings);
  }

  private static async Task<IEnumerable<string>> ResolveTypeNamespacesAsync(
      Project project,
      Compilation compilation,
      SyntaxNode node,
      CancellationToken cancellationToken)
  {
    var simpleName = node as SimpleNameSyntax
        ?? node.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().FirstOrDefault();
    if (simpleName is null)
    {
      return [];
    }

    var name = simpleName.Identifier.ValueText;
    var arity = (simpleName as GenericNameSyntax)?.Arity ?? 0;

    var declarations = await SymbolFinder.FindDeclarationsAsync(
        project, name, ignoreCase: false, SymbolFilter.Type, cancellationToken).ConfigureAwait(false);

    return declarations
        .OfType<INamedTypeSymbol>()
        .Where(t => t.Arity == arity)
        .Where(t => IsAccessible(t, compilation))
        .Where(t => !t.ContainingNamespace.IsGlobalNamespace)
        .Select(t => t.ContainingNamespace.ToDisplayString())
        .Distinct();
  }

  private static async Task<IEnumerable<string>> ResolveExtensionMethodNamespacesAsync(
      Project project,
      SemanticModel semanticModel,
      Compilation compilation,
      SyntaxNode node,
      CancellationToken cancellationToken)
  {
    var memberAccess = node as MemberAccessExpressionSyntax
        ?? node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
    if (memberAccess is null)
    {
      return [];
    }

    var memberName = memberAccess.Name.Identifier.ValueText;
    var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
    if (receiverType is null)
    {
      return [];
    }

    var declarations = await SymbolFinder.FindDeclarationsAsync(
        project, memberName, ignoreCase: false, SymbolFilter.Member, cancellationToken).ConfigureAwait(false);

    return declarations
        .OfType<IMethodSymbol>()
        .Where(m => m.IsExtensionMethod)
        .Where(m => IsAccessible(m, compilation))
        .Where(m => m.ReduceExtensionMethod(receiverType) is not null)
        .Where(m => !m.ContainingNamespace.IsGlobalNamespace)
        .Select(m => m.ContainingNamespace.ToDisplayString())
        .Distinct();
  }

  private static bool IsAccessible(ISymbol symbol, Compilation compilation)
    => symbol.DeclaredAccessibility == Accessibility.Public
       || SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly);

  private static HashSet<string> GetNamespacesInScope(SyntaxNode root)
  {
    var set = new HashSet<string>(StringComparer.Ordinal);

    foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
    {
      if (directive.Alias is null && directive.StaticKeyword.IsKind(SyntaxKind.None) && directive.Name is not null)
      {
        set.Add(directive.Name.ToString());
      }
    }

    foreach (var declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
    {
      set.Add(declaration.Name.ToString());
    }

    return set;
  }
}
