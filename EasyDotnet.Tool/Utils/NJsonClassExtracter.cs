
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.Utils;

public static class NJsonClassExtractor
{
  public static string ExtractClassWithNamespace(string generatedCode, string targetNamespace)
  {
    var tree = CSharpSyntaxTree.ParseText(generatedCode);
    var root = tree.GetCompilationUnitRoot();

    var classDeclaration = root.DescendantNodes()
        .OfType<ClassDeclarationSyntax>()
        .FirstOrDefault() ?? throw new System.Exception("Json contains no class definition");

    var cleanClass = classDeclaration.WithAttributeLists(
        SyntaxFactory.List<AttributeListSyntax>());

    var cleanProperties = cleanClass.Members
        .OfType<PropertyDeclarationSyntax>()
        .Select(prop => prop.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()))
        .Cast<MemberDeclarationSyntax>()
        .ToList();

    cleanClass = cleanClass.WithMembers(SyntaxFactory.List(cleanProperties));

    var modifiers = cleanClass.Modifiers.Where(m => !m.IsKind(SyntaxKind.PartialKeyword));
    cleanClass = cleanClass.WithModifiers(SyntaxFactory.TokenList(modifiers));

    if (!string.IsNullOrEmpty(targetNamespace))
    {
      var fileScopedNamespace = SyntaxFactory.FileScopedNamespaceDeclaration(
          SyntaxFactory.IdentifierName(targetNamespace))
          .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(cleanClass));

      var compilationUnit = SyntaxFactory.CompilationUnit()
          .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(fileScopedNamespace));

      return compilationUnit.NormalizeWhitespace().ToFullString();
    }

    var formattedClass = cleanClass.NormalizeWhitespace().ToFullString();

    return formattedClass;
  }
}