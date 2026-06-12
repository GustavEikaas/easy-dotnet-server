using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.RoslynLanguageServices.EfQuery;

public sealed record EfQueryDetection(ExpressionSyntax QueryExpression, List<ExpressionSyntax> ContextNodes);

/// <summary>
/// Locates an EF Core query (an IQueryable expression rooted in a DbSet access on a DbContext) at a
/// cursor position. Shared as a linked source file between the Roslyn LSP extension (code action
/// detection) and EasyDotnet.IDE (the roslyn/ef-generated-sql endpoint) so both agree on what
/// counts as a query.
/// </summary>
public static class EfQueryDetector
{
  /// <summary>
  /// Strict pass first (cursor token inside the query expression chain), then a statement-wide
  /// fallback so a cursor anywhere on the statement's lines (indentation, var, await) still finds
  /// the query.
  /// </summary>
  public static EfQueryDetection? FindQuery(SyntaxNode root, SemanticModel semanticModel, int position, CancellationToken cancellationToken)
  {
    var token = root.FindToken(position);

    var strict = Detect(
      token.Parent?.AncestorsAndSelf().OfType<ExpressionSyntax>() ?? [],
      semanticModel,
      cancellationToken);
    if (strict is not null)
    {
      return strict;
    }

    var scope = token.Parent?.AncestorsAndSelf().FirstOrDefault(x => x is StatementSyntax or ArrowExpressionClauseSyntax);
    return scope is null
      ? null
      : Detect(scope.DescendantNodes().OfType<ExpressionSyntax>(), semanticModel, cancellationToken);
  }

  private static EfQueryDetection? Detect(IEnumerable<ExpressionSyntax> nodes, SemanticModel semanticModel, CancellationToken cancellationToken)
  {
    var queryExpression = nodes
      .Where(x => x is not TypeSyntax type || !SyntaxFacts.IsInTypeOnlyContext(type))
      .SelectMany(x => EnumerateQueryableCandidates(x, semanticModel, cancellationToken))
      .OrderBy(x => x.Span.Length)
      .LastOrDefault();

    if (queryExpression is null)
    {
      return null;
    }

    var contextNodes = FindContextNodes(queryExpression, semanticModel, cancellationToken);
    return contextNodes.Count == 0 ? null : new EfQueryDetection(queryExpression, contextNodes);
  }

  /// <summary>
  /// Outermost DbContext-typed expressions inside the query (e.g. db, this._context); these are the
  /// roots the query hangs off and what the SQL endpoint rewrites to its context placeholder.
  /// </summary>
  private static List<ExpressionSyntax> FindContextNodes(ExpressionSyntax queryExpression, SemanticModel semanticModel, CancellationToken cancellationToken) =>
    [.. queryExpression
      .DescendantNodesAndSelf()
      .OfType<ExpressionSyntax>()
      .Where(x => IsDbContext(semanticModel.GetTypeInfo(x, cancellationToken).Type))
      .Where(x => !x.Ancestors()
        .TakeWhile(a => a != queryExpression.Parent)
        .OfType<ExpressionSyntax>()
        .Any(a => IsDbContext(semanticModel.GetTypeInfo(a, cancellationToken).Type)))];

  /// <summary>
  /// Yields the node itself when it is IQueryable, and additionally the receiver of a member
  /// access/invocation when only the receiver is IQueryable. The latter covers cursors placed on
  /// terminating operators (ToListAsync, Count, ...) whose own type is no longer IQueryable.
  /// </summary>
  private static IEnumerable<ExpressionSyntax> EnumerateQueryableCandidates(ExpressionSyntax node, SemanticModel semanticModel, CancellationToken cancellationToken)
  {
    if (IsQueryable(semanticModel.GetTypeInfo(node, cancellationToken).Type))
    {
      yield return node;
    }

    var receiver = node switch
    {
      MemberAccessExpressionSyntax member => member.Expression,
      InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } => member.Expression,
      _ => null
    };

    if (receiver is not null && IsQueryable(semanticModel.GetTypeInfo(receiver, cancellationToken).Type))
    {
      yield return receiver;
    }
  }

  private static bool IsQueryable(ITypeSymbol? type) =>
    type is INamedTypeSymbol named && (IsIQueryableSymbol(named) || named.AllInterfaces.Any(IsIQueryableSymbol));

  private static bool IsIQueryableSymbol(INamedTypeSymbol symbol) =>
    symbol.Name == "IQueryable" && symbol.ContainingNamespace.ToDisplayString() == "System.Linq";

  private static bool IsDbContext(ITypeSymbol? type)
  {
    for (var current = type; current is not null; current = current.BaseType)
    {
      if (current.Name == "DbContext" && current.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore")
      {
        return true;
      }
    }
    return false;
  }
}