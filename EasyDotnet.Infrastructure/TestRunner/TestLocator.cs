using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
namespace EasyDotnet.Infrastructure.TestRunner;

public static class RoslynLocator
{
  public static string? GetMethodSignatureAtLine(string code, int lineOneBased)
  {
    var tree = CSharpSyntaxTree.ParseText(code);
    var root = tree.GetRoot();
    var sourceText = SourceText.From(code);

    // Convert 1-based line to 0-based index for Roslyn
    var lineIndex = lineOneBased - 1;

    if (lineIndex < 0 || lineIndex >= sourceText.Lines.Count)
      throw new ArgumentOutOfRangeException(nameof(lineOneBased), "Line number out of range");

    // Get the position of the start of the line
    var lineSpan = sourceText.Lines[lineIndex].Span;

    // Find the node at the start of that line (or the first token on that line)
    var token = root.FindToken(lineSpan.Start);
    var node = token.Parent;

    // Walk up to find the method declaration
    var method = node?.AncestorsAndSelf()
                      .OfType<MethodDeclarationSyntax>()
                      .FirstOrDefault();

    if (method == null) return null;

    var classDecl = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
    return $"{classDecl?.Identifier.Text ?? "Global"}.{method.Identifier.Text}";
  }

  public static int? FindLineForMethod(string code, string signature)
  {
    var parts = signature.Split('.');
    var className = parts[0];
    var methodName = parts[1];

    var tree = CSharpSyntaxTree.ParseText(code);
    var root = tree.GetRoot();

    var method = root.DescendantNodes()
                     .OfType<MethodDeclarationSyntax>()
                     .FirstOrDefault(m =>
                         m.Identifier.Text == methodName &&
                         (m.Parent as ClassDeclarationSyntax)?.Identifier.Text == className);

    if (method == null) return null;

    // Get the starting line of the method (this usually includes attributes)
    // Verify if you want the attribute line or the 'public async...' line.
    // GetLocation().GetLineSpan() usually starts at the first attribute if present.
    var lineSpan = method.GetLocation().GetLineSpan();

    // Convert 0-based Roslyn line back to 1-based
    return lineSpan.StartLinePosition.Line + 1;
  }
}