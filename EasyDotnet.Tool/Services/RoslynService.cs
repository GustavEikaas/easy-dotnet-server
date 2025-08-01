using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Controllers.Roslyn;
using EasyDotnet.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace EasyDotnet.Services;

public sealed record VariableResult(string Identifier, int LineStart, int LineEnd, int ColumnStart, int ColumnEnd);

public class RoslynService(RoslynProjectMetadataCache cache)
{
  private async Task<ProjectCacheItem> GetOrSetProjectFromCache(string projectPath, CancellationToken cancellationToken)
  {
    if (cache.TryGet(projectPath, out var cachedProject) && cachedProject is not null)
    {
      return cachedProject;
    }

    using var workspace = MSBuildWorkspace.Create();
    var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken) ?? throw new Exception($"Failed to load project at path: {projectPath}");
    cache.Set(projectPath, project);

    return !cache.TryGet(projectPath, out var updatedProject) || updatedProject is null
      ? throw new Exception("Caching failed after setting project metadata.")
      : updatedProject;
  }

  public async Task<List<VariableResult>> AnalyzeAsync(string sourceFilePath, int lineNumber)
  {
    using var workspace = MSBuildWorkspace.Create();

    var csprojPath = FindCsprojFromFile(sourceFilePath);
    var project = await workspace.OpenProjectAsync(csprojPath);
    var document = project.Documents.FirstOrDefault(d => string.Equals(d.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("Document not found.");
    var root = await document.GetSyntaxRootAsync();
    var semanticModel = await document.GetSemanticModelAsync();

    if (root == null || semanticModel == null)
      throw new Exception("Unable to load syntax/semantic model.");

    // Find innermost executable node (method, lambda, local func) containing the line
    var executableNodes = root.DescendantNodes()
        .Where(n =>
            n is BaseMethodDeclarationSyntax ||
            n is AnonymousFunctionExpressionSyntax ||
            n is LocalFunctionStatementSyntax);

    SyntaxNode? scopeNode = null;

    foreach (var node in executableNodes)
    {
      var span = node.GetLocation().GetLineSpan().Span;
      var start = span.Start.Line + 1;
      var end = span.End.Line + 1;

      if (lineNumber >= start && lineNumber <= end)
      {
        if (scopeNode == null || node.Span.Length < scopeNode.Span.Length)
        {
          // Choose the *smallest* containing node to get innermost scope
          scopeNode = node;
        }
      }
    }

    if (scopeNode == null)
      throw new Exception($"No executable scope found at line {lineNumber}");

    // Use position at start of the scope node body (or node span start if no body)
    var position = scopeNode switch
    {
      BaseMethodDeclarationSyntax m => m.Body?.OpenBraceToken.Span.End ?? m.SpanStart,
      AnonymousFunctionExpressionSyntax a => a.Body?.SpanStart ?? a.SpanStart,
      LocalFunctionStatementSyntax l => l.Body?.OpenBraceToken.Span.End ?? l.SpanStart,
      _ => scopeNode.SpanStart
    };

    // Lookup locals and parameters visible at this position
    var symbolsInScope = semanticModel.LookupSymbols(position)
        .Where(s => s.Kind == SymbolKind.Local || s.Kind == SymbolKind.Parameter)
        .Distinct(SymbolEqualityComparer.Default);

    var results = new List<VariableResult>();

    foreach (var symbol in symbolsInScope)
    {
      var location = symbol.Locations.FirstOrDefault();
      if (location == null || !location.IsInSource)
        continue;

      var symbolSpan = location.GetLineSpan().Span;

      results.Add(new VariableResult(
          Identifier: symbol.Name,
          LineStart: symbolSpan.Start.Line + 1,
          LineEnd: symbolSpan.End.Line + 1,
          ColumnStart: symbolSpan.Start.Character + 1,
          ColumnEnd: symbolSpan.End.Character + 1
      ));
    }

    return results;
  }

  public async Task<bool> BootstrapFile(string filePath, Kind kind, bool preferFileScopedNamespace, CancellationToken cancellationToken)
  {
    var projectPath = FindCsprojFromFile(filePath);
    var project = await GetOrSetProjectFromCache(projectPath, cancellationToken);

    var rootNamespace = project.RootNamespace;

    var useFileScopedNs = preferFileScopedNamespace && project.SupportsFileScopedNamespace;

    var relativePath = Path.GetDirectoryName(filePath)!
        .Replace(Path.GetDirectoryName(projectPath)!, "")
        .Trim(Path.DirectorySeparatorChar);
    var nsSuffix = relativePath.Replace(Path.DirectorySeparatorChar, '.');
    var fullNamespace = string.IsNullOrEmpty(nsSuffix) ? rootNamespace : $"{rootNamespace}.{nsSuffix}";

    var className = Path.GetFileNameWithoutExtension(filePath).Split(".").ElementAt(0)!;

    var typeDecl = CreateTypeDeclaration(kind, className);

    MemberDeclarationSyntax nsDeclaration = useFileScopedNs
        ? SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
        : SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace));

    var unit = SyntaxFactory.CompilationUnit()
      .AddMembers(nsDeclaration)
      .AddMembers(typeDecl)
      .NormalizeWhitespace(eol: Environment.NewLine);

    if (preferFileScopedNamespace)
    {
      unit = unit.AddNewLinesAfterNamespaceDeclaration();
    }

    if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
    {
      return false;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    File.WriteAllText(filePath, unit.ToFullString());

    return true;
  }

  private static string FindCsprojFromFile(string filePath)
  {
    var dir = Path.GetDirectoryName(filePath)
        ?? throw new ArgumentException("Invalid file path", nameof(filePath));

    return FindCsprojInDirectoryOrParents(dir)
        ?? throw new FileNotFoundException($"Failed to resolve csproj for file: {filePath}");
  }

  private static string? FindCsprojInDirectoryOrParents(string directory)
  {
    var csproj = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
    if (csproj != null)
    {
      return csproj;
    }

    var parent = Directory.GetParent(directory);
    return parent != null
        ? FindCsprojInDirectoryOrParents(parent.FullName)
        : null;
  }

  private static MemberDeclarationSyntax CreateTypeDeclaration(Kind kind, string className)
  {
    var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

    return kind switch
    {
      Kind.Class => SyntaxFactory.ClassDeclaration(className)
          .WithModifiers(modifiers)
          .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
          .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)),

      Kind.Interface => SyntaxFactory.InterfaceDeclaration(className)
          .WithModifiers(modifiers),

      Kind.Record => SyntaxFactory.RecordDeclaration(
              SyntaxFactory.Token(SyntaxKind.RecordKeyword),
              SyntaxFactory.Identifier(className))
          .WithModifiers(modifiers)
          .WithParameterList(SyntaxFactory.ParameterList())
          .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),

      _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
  }
}