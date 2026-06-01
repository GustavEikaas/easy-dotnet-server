using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.RoslynLanguageServices.Rename;

public static class RenameFileDecisionService
{
  public static async Task<ShouldRenameFileResponse> ShouldRenameFileAsync(
      Document document,
      int line,
      int character,
      string newName,
      CancellationToken cancellationToken)
  {
    if (!SyntaxFacts.IsValidIdentifier(newName))
    {
      return ShouldRenameFileResponse.No("New name is not a valid C# identifier.");
    }

    var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
    if (line < 0 || line >= text.Lines.Count)
    {
      return ShouldRenameFileResponse.No("Line is outside the document.");
    }

    var textLine = text.Lines[line];
    if (character < 0 || character > textLine.Span.Length)
    {
      return ShouldRenameFileResponse.No("Character is outside the line.");
    }

    var position = textLine.Start + character;
    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
    if (root is null || semanticModel is null)
    {
      return ShouldRenameFileResponse.No("Could not resolve semantic model.");
    }

    var token = root.FindToken(position);
    var symbol = ResolveNamedTypeSymbol(semanticModel, token, cancellationToken);
    if (symbol is null)
    {
      return ShouldRenameFileResponse.No("Rename target is not a type.");
    }

    symbol = symbol.OriginalDefinition;
    if (symbol.ContainingType is not null)
    {
      return ShouldRenameFileResponse.No("Nested types are not renamed with files.");
    }

    if (symbol.DeclaringSyntaxReferences.Length != 1)
    {
      return ShouldRenameFileResponse.No("Type has zero or multiple declarations.");
    }

    var declaration = await symbol.DeclaringSyntaxReferences[0]
        .GetSyntaxAsync(cancellationToken)
        .ConfigureAwait(false);

    if (declaration is not BaseTypeDeclarationSyntax typeDeclaration)
    {
      return ShouldRenameFileResponse.No("Type declaration is not a file-backed C# type.");
    }

    if (typeDeclaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any())
    {
      return ShouldRenameFileResponse.No("Nested types are not renamed with files.");
    }

    var declaringDocument = document.Project.Solution.GetDocument(typeDeclaration.SyntaxTree);
    if (declaringDocument?.FilePath is not { Length: > 0 } filePath)
    {
      return ShouldRenameFileResponse.No("Declaring document has no file path.");
    }

    if (!IsNormalCSharpFile(filePath))
    {
      return ShouldRenameFileResponse.No("Declaring document is not a normal C# file.");
    }

    var oldName = symbol.Name;
    if (string.Equals(oldName, newName, StringComparison.Ordinal))
    {
      return ShouldRenameFileResponse.No("New type name matches old type name.");
    }

    if (!string.Equals(Path.GetFileNameWithoutExtension(filePath), oldName, StringComparison.Ordinal))
    {
      return ShouldRenameFileResponse.No("File name does not exactly match the type name.");
    }

    var declaringRoot = await declaringDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
    if (declaringRoot is null)
    {
      return ShouldRenameFileResponse.No("Could not resolve declaring syntax root.");
    }

    var matchingFileNameTypes = declaringRoot
        .DescendantNodes()
        .OfType<BaseTypeDeclarationSyntax>()
        .Where(IsTopLevelFileNameType)
        .Where(type => string.Equals(type.Identifier.ValueText, oldName, StringComparison.Ordinal))
        .ToArray();

    if (matchingFileNameTypes.Length != 1 || !IsSameDeclaration(matchingFileNameTypes[0], typeDeclaration))
    {
      return ShouldRenameFileResponse.No("Renamed type is not the unique top-level type matching the file name.");
    }

    var directory = Path.GetDirectoryName(filePath);
    if (string.IsNullOrWhiteSpace(directory))
    {
      return ShouldRenameFileResponse.No("Declaring document has no directory.");
    }

    var newPath = Path.Combine(directory, newName + ".cs");
    if (PathsEqual(filePath, newPath))
    {
      return ShouldRenameFileResponse.No("Target file path matches current file path.");
    }

    if (File.Exists(newPath) || Directory.Exists(newPath))
    {
      return ShouldRenameFileResponse.No("Target file already exists.");
    }

    return ShouldRenameFileResponse.Yes(filePath, newPath, "Primary type name matches file name.");
  }

  private static INamedTypeSymbol? ResolveNamedTypeSymbol(
      SemanticModel semanticModel,
      SyntaxToken token,
      CancellationToken cancellationToken)
  {
    foreach (var node in token.Parent?.AncestorsAndSelf() ?? [])
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (node is BaseTypeDeclarationSyntax typeDeclaration &&
          typeDeclaration.Identifier == token &&
          semanticModel.GetDeclaredSymbol(node, cancellationToken) is INamedTypeSymbol declaredType)
      {
        return declaredType;
      }

      var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
      var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.SingleOrDefault();
      if (symbol is INamedTypeSymbol namedType)
      {
        return namedType;
      }

      if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } constructedType })
      {
        return constructedType;
      }
    }

    return null;
  }

  private static bool IsTopLevelFileNameType(BaseTypeDeclarationSyntax type)
    => type is (ClassDeclarationSyntax or InterfaceDeclarationSyntax or RecordDeclarationSyntax) &&
       !type.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any();

  private static bool IsSameDeclaration(BaseTypeDeclarationSyntax left, BaseTypeDeclarationSyntax right)
    => left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;

  private static bool IsNormalCSharpFile(string filePath)
  {
    if (!string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var fileName = Path.GetFileName(filePath);
    if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return !segments.Any(static segment => string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
  }

  private static bool PathsEqual(string left, string right)
  {
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
  }
}