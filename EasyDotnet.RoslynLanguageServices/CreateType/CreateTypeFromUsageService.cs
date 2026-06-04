using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.RoslynLanguageServices.CreateType;

public static class CreateTypeFromUsageService
{
  public static async Task<CreateTypeFromUsageResponse> CreateTypeFromUsageAsync(
      Document document,
      int line,
      int character,
      CancellationToken cancellationToken)
  {
    var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
    if (line < 0 || line >= text.Lines.Count)
    {
      return CreateTypeFromUsageResponse.No("Line is outside the document.");
    }

    var textLine = text.Lines[line];
    if (character < 0 || character > textLine.Span.Length)
    {
      return CreateTypeFromUsageResponse.No("Character is outside the line.");
    }

    if (document.FilePath is not { Length: > 0 } filePath)
    {
      return CreateTypeFromUsageResponse.No("Document has no file path.");
    }

    var directory = Path.GetDirectoryName(filePath);
    if (string.IsNullOrWhiteSpace(directory))
    {
      return CreateTypeFromUsageResponse.No("Document has no directory.");
    }

    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
    if (root is null || semanticModel is null)
    {
      return CreateTypeFromUsageResponse.No("Could not resolve semantic model.");
    }

    var position = textLine.Start + character;
    var token = root.FindToken(position);
    var name = FindUnresolvedTypeName(semanticModel, token, cancellationToken);
    if (name is null)
    {
      return CreateTypeFromUsageResponse.No("Cursor is not on an unresolved type usage.");
    }

    var typeName = name.Identifier.ValueText;
    if (!SyntaxFacts.IsValidIdentifier(typeName))
    {
      return CreateTypeFromUsageResponse.No("Type name is not a valid C# identifier.");
    }

    var targetPath = Path.Combine(directory, typeName + ".cs");
    if (File.Exists(targetPath) || Directory.Exists(targetPath))
    {
      return CreateTypeFromUsageResponse.No("Target file already exists.");
    }

    var fileText = CreateFileText(typeName, FindNamespace(name));
    return CreateTypeFromUsageResponse.Yes(typeName, targetPath, fileText);
  }

  private static SimpleNameSyntax? FindUnresolvedTypeName(
      SemanticModel semanticModel,
      SyntaxToken token,
      CancellationToken cancellationToken)
  {
    foreach (var node in token.Parent?.AncestorsAndSelf() ?? [])
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (node is not SimpleNameSyntax name || name.Identifier != token)
      {
        continue;
      }

      if (!IsTypePosition(name))
      {
        continue;
      }

      var symbolInfo = semanticModel.GetSymbolInfo(name, cancellationToken);
      if (symbolInfo.Symbol is not null || !symbolInfo.CandidateSymbols.IsEmpty)
      {
        return null;
      }

      return name;
    }

    return null;
  }

  private static bool IsTypePosition(SimpleNameSyntax name)
  {
    SyntaxNode current = name;
    while (true)
    {
      if (current.Parent is TypeArgumentListSyntax { Parent: GenericNameSyntax genericName })
      {
        current = genericName;
        continue;
      }

      if (current.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or GenericNameSyntax or NullableTypeSyntax or ArrayTypeSyntax)
      {
        current = current.Parent;
        continue;
      }

      break;
    }

    return current.Parent switch
    {
      ObjectCreationExpressionSyntax objectCreation => objectCreation.Type == current,
      VariableDeclarationSyntax variableDeclaration => variableDeclaration.Type == current,
      ParameterSyntax parameter => parameter.Type == current,
      PropertyDeclarationSyntax property => property.Type == current,
      MethodDeclarationSyntax method => method.ReturnType == current,
      LocalFunctionStatementSyntax localFunction => localFunction.ReturnType == current,
      CastExpressionSyntax cast => cast.Type == current,
      DefaultExpressionSyntax defaultExpression => defaultExpression.Type == current,
      TypeOfExpressionSyntax typeOf => typeOf.Type == current,
      BaseTypeSyntax baseType => baseType.Type == current,
      TypeConstraintSyntax typeConstraint => typeConstraint.Type == current,
      _ => false,
    };
  }

  private static string? FindNamespace(SyntaxNode node)
    => node.Ancestors()
        .OfType<BaseNamespaceDeclarationSyntax>()
        .FirstOrDefault()
        ?.Name
        .ToString();

  private static string CreateFileText(string typeName, string? namespaceName)
  {
    var typeText = $"public class {typeName}\n{{\n}}\n";

    return string.IsNullOrWhiteSpace(namespaceName)
        ? typeText
        : $"namespace {namespaceName};\n\n{typeText}";
  }
}