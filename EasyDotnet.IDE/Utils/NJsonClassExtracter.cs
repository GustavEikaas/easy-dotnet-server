using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.Utils;

public static class NJsonClassExtractor
{
  public static string ExtractClassesWithNamespace(string generatedCode, string targetNamespace, bool preferFileScopedNamespace)
  {
    var tree = CSharpSyntaxTree.ParseText(generatedCode);
    var root = tree.GetCompilationUnitRoot();

    var usingDirective = SyntaxFactory.UsingDirective(
        SyntaxFactory.ParseName("System.Text.Json.Serialization"));

    var cleanedClasses = root.DescendantNodes()
        .OfType<ClassDeclarationSyntax>()
        .Select(CleanClassDeclaration)
        .Cast<MemberDeclarationSyntax>()
        .ToList();

    if (cleanedClasses.Count == 0)
    {
      throw new Exception("Json contains no class definitions");
    }

    var namespaceDecl = preferFileScopedNamespace
        ? SyntaxFactory.FileScopedNamespaceDeclaration(
              SyntaxFactory.IdentifierName(targetNamespace))
            .WithMembers(SyntaxFactory.List(cleanedClasses))
        : (MemberDeclarationSyntax)SyntaxFactory.NamespaceDeclaration(
              SyntaxFactory.IdentifierName(targetNamespace))
            .WithMembers(SyntaxFactory.List(cleanedClasses));

    var compilationUnit = SyntaxFactory.CompilationUnit()
        .WithUsings(SyntaxFactory.SingletonList(usingDirective))
        .WithMembers(SyntaxFactory.SingletonList(namespaceDecl));

    return compilationUnit.NormalizeWhitespace().ToFullString();
  }

  private static bool IsAdditionalPropertiesField(PropertyDeclarationSyntax prop)
  {
    if (prop.Identifier.Text != "AdditionalProperties")
    {
      return false;
    }

    var typeString = prop.Type.ToString();
    return typeString == "System.Collections.Generic.IDictionary<string, object>";
  }

  private static ClassDeclarationSyntax CleanClassDeclaration(ClassDeclarationSyntax classDecl)
  {
    var cleanedClass = classDecl.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>());

    var modifiers = cleanedClass.Modifiers.Where(m => !m.IsKind(SyntaxKind.PartialKeyword));
    cleanedClass = cleanedClass.WithModifiers(SyntaxFactory.TokenList(modifiers));

    var cleanedProperties = classDecl.Members
        .OfType<PropertyDeclarationSyntax>()
        .Where(prop => !IsAdditionalPropertiesField(prop))
        .Select(CleanPropertyDeclaration)
        .Cast<MemberDeclarationSyntax>()
        .ToList();

    return cleanedClass.WithMembers(SyntaxFactory.List(cleanedProperties));
  }

  private static PropertyDeclarationSyntax CleanPropertyDeclaration(PropertyDeclarationSyntax prop)
  {
    var jsonPropertyAttributes = prop.AttributeLists
        .SelectMany(attrList => attrList.Attributes)
        .Where(attr =>
            attr.Name.ToString().EndsWith("JsonPropertyName") ||
            attr.Name.ToString().EndsWith("JsonPropertyNameAttribute"))
        .Select(attr =>
            SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("JsonPropertyName"))
                .WithArgumentList(attr.ArgumentList));

    var newAttributeList = SyntaxFactory.AttributeList(SyntaxFactory.SeparatedList(jsonPropertyAttributes));

    return prop.WithAttributeLists(
        newAttributeList.Attributes.Count > 0
            ? SyntaxFactory.SingletonList(newAttributeList)
            : SyntaxFactory.List<AttributeListSyntax>());
  }
}