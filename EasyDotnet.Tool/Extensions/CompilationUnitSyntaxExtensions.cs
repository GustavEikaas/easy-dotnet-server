using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.Extensions;

public static class CompilationUnitSyntaxExtensions
{
  public static CompilationUnitSyntax AddNewLinesAfterNamespaceDeclaration(this CompilationUnitSyntax input)
  {
    var oldNode = input
      .DescendantNodes()
      .OfType<FileScopedNamespaceDeclarationSyntax>()
      .FirstOrDefault();
    if (oldNode is null)
    {
      return input;
    }

    var newNode = oldNode
      .WithTrailingTrivia(SyntaxFactory.LineFeed, SyntaxFactory.LineFeed);

    return input.ReplaceNode(oldNode, newNode);
  }
}