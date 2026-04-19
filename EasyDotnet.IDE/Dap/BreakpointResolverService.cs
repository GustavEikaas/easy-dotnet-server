using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EasyDotnet.IDE.Dap;

public record BreakpointCandidate(int Line, int Column, string TargetText, string ContextPreview);

public static class BreakpointResolverService
{
  public static async Task<List<BreakpointCandidate>> GetCandidatesAsync(
      string filePath,
      int requestedLine,
      CancellationToken cancellationToken = default)
  {
    if (!File.Exists(filePath)) return [];

    var code = await File.ReadAllTextAsync(filePath, cancellationToken);
    var tree = CSharpSyntaxTree.ParseText(code, cancellationToken: cancellationToken);
    var root = await tree.GetRootAsync(cancellationToken);
    var text = await tree.GetTextAsync(cancellationToken);

    if (requestedLine < 0 || requestedLine >= text.Lines.Count)
    {
      return [];
    }

    var lineSpan = text.Lines[requestedLine].Span;

    var nodes = root.DescendantNodes(lineSpan)
        .Where(n => lineSpan.Contains(n.SpanStart) && IsBreakable(n))
        .ToList();

    if (!nodes.Any())
    {
      var searchSpan = new TextSpan(lineSpan.End, root.FullSpan.End - lineSpan.End);
      var nextValidNode = root.DescendantNodes(searchSpan).FirstOrDefault(IsBreakable);

      if (nextValidNode != null)
      {
        nodes.Add(nextValidNode);
      }
    }

    var candidates = new List<BreakpointCandidate>();

    foreach (var node in nodes)
    {
      var targetNode = ExtractTargetSpan(node);
      var linePos = text.Lines.GetLinePosition(targetNode.SpanStart);

      var targetText = targetNode.ToString().Replace("\r\n", "\n").Replace("\r", "\n");

      var startLine = Math.Max(0, linePos.Line - 2);
      var endLine = Math.Min(text.Lines.Count - 1, linePos.Line + 2);
      var contextSpan = TextSpan.FromBounds(text.Lines[startLine].Start, text.Lines[endLine].End);
      var contextPreview = text.ToString(contextSpan).Replace("\r\n", "\n").Replace("\r", "\n");

      candidates.Add(new BreakpointCandidate(linePos.Line, linePos.Character, targetText, contextPreview));
    }

    return [.. candidates.GroupBy(c => c.Column).Select(g => g.First())];
  }

  private static bool IsBreakable(SyntaxNode node)
  {
    if (node is BlockSyntax) return false;

    return node is StatementSyntax ||
           node is AnonymousFunctionExpressionSyntax ||
           node is ArrowExpressionClauseSyntax ||
           (node is AccessorDeclarationSyntax acc && (acc.Body != null || acc.ExpressionBody != null));
  }

  private static SyntaxNode ExtractTargetSpan(SyntaxNode node)
  {
    if (node is AnonymousFunctionExpressionSyntax lambda && lambda.Block == null)
    {
      return lambda.ExpressionBody ?? node;
    }

    if (node is ArrowExpressionClauseSyntax arrow)
    {
      return arrow.Expression;
    }

    return node;
  }
}