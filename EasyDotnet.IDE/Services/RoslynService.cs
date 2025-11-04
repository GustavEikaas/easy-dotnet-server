using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers.Roslyn;
using EasyDotnet.Extensions;
using EasyDotnet.MsBuild;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services;

public sealed record VariableResult(string Identifier, int LineStart, int LineEnd, int ColumnStart, int ColumnEnd);

public class RoslynService(IMsBuildService service, ILogger<RoslynService> logService)
{
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

    var scopeNode = executableNodes
        .Where(node =>
        {
          var span = node.GetLocation().GetLineSpan().Span;
          var start = span.Start.Line + 1;
          var end = span.End.Line + 1;
          return lineNumber >= start && lineNumber <= end;
        })
        .OrderBy(node => node.Span.Length) // Innermost = smallest span
        .FirstOrDefault();

    if (scopeNode == null)
    {
      // Top scope, e.g., Program.cs
      return [];
    }

    var text = await document.GetTextAsync();
    var line = text.Lines[Math.Clamp(lineNumber - 1, 0, text.Lines.Count - 1)];
    var position = line.Start;

    // Lookup locals and parameters visible at this position
    var symbolsInScope = semanticModel.LookupSymbols(position)
        .Where(s => s.Kind == SymbolKind.Local || s.Kind == SymbolKind.Parameter)
        .Distinct(SymbolEqualityComparer.Default);

    var results = symbolsInScope
        .Select(symbol => new
        {
          Symbol = symbol,
          Location = symbol.Locations.FirstOrDefault()
        })
        .Where(x => x.Location != null && x.Location.IsInSource)
        .Select(x => new
        {
          x.Symbol,
          x.Location!.GetLineSpan().Span
        })
        .Where(x => x.Span.Start.Line < lineNumber - 1)
        .Select(x => new VariableResult(
            Identifier: x.Symbol.Name,
            LineStart: x.Span.Start.Line + 1,
            LineEnd: x.Span.End.Line + 1,
            ColumnStart: x.Span.Start.Character + 1,
            ColumnEnd: x.Span.End.Character + 1
        ))
        .ToList();

    var thisVariable = TryResolveThis(semanticModel, root, position);
    if (thisVariable != null)
    {
      results.Add(thisVariable);
    }

    return results;
  }

  private VariableResult? TryResolveThis(SemanticModel semanticModel, SyntaxNode root, int position) =>
    semanticModel.GetEnclosingSymbol(position) switch
    {
      IMethodSymbol { IsStatic: false, ContainingType: var type } when type is not null =>
          root.FindToken(position).Parent?.AncestorsAndSelf()
              .OfType<ClassDeclarationSyntax>()
              .FirstOrDefault() is { } classNode
              ? CreateThisVariableResult(classNode)
              : null,

      _ => null
    };

  private static VariableResult CreateThisVariableResult(ClassDeclarationSyntax classNode)
  {
    var span = classNode.Identifier.GetLocation().GetLineSpan().Span;
    return new VariableResult(
        Identifier: "this",
        LineStart: span.Start.Line + 1,
        LineEnd: span.End.Line + 1,
        ColumnStart: span.Start.Character + 1,
        ColumnEnd: span.End.Character + 1
    );
  }

  public async Task<bool> BootstrapFile(string filePath, Kind kind, bool preferFileScopedNamespace, CancellationToken cancellationToken)
  {
    var projectPath = FindCsprojFromFile(filePath);

    var project = await service.GetOrSetProjectPropertiesAsync(projectPath, null, "Debug", cancellationToken);

    var rootNamespace = project.RootNamespace ?? project.ProjectName;

    var supportsFileScopedNamespace =
        string.IsNullOrEmpty(project.LangVersion) ||
        string.Compare(project.LangVersion, "10.0", StringComparison.OrdinalIgnoreCase) >= 0 ||
        project.LangVersion.Equals("latest", StringComparison.OrdinalIgnoreCase);

    var useFileScopedNs = preferFileScopedNamespace && supportsFileScopedNamespace;

    var relativePath = Path.GetDirectoryName(filePath)!
        .Replace(Path.GetDirectoryName(projectPath)!, "")
        .Trim(Path.DirectorySeparatorChar);
    var nsSuffix = relativePath.Replace(Path.DirectorySeparatorChar, '.');
    var fullNamespace = string.IsNullOrEmpty(nsSuffix) ? rootNamespace : $"{rootNamespace}.{nsSuffix}";

    var className = Path.GetFileNameWithoutExtension(filePath).Split(".").ElementAt(0)!;

    var typeDecl = CreateTypeDeclaration(kind, className);

    BaseNamespaceDeclarationSyntax nsDeclaration = useFileScopedNs
        ? SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
              .AddMembers(typeDecl)
        : SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
              .AddMembers(typeDecl);

    var unit = SyntaxFactory.CompilationUnit()
        .AddMembers(nsDeclaration)
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

  public async IAsyncEnumerable<DiagnosticMessage> GetWorkspaceDiagnosticsAsync(
    string targetPath,
    bool includeWarnings)
  {
    if (string.IsNullOrWhiteSpace(targetPath))
      throw new ArgumentException("Project path must be provided", nameof(targetPath));

    if (!File.Exists(targetPath))
      throw new FileNotFoundException($"Project or solution file not found: {targetPath}");

    using var workspace = MSBuildWorkspace.Create();

    Solution solution;

    try
    {
      if (FileTypes.IsAnySolutionFile(targetPath))
      {
        solution = await workspace.OpenSolutionAsync(targetPath).ConfigureAwait(false);
      }
      else if (FileTypes.IsAnyProjectFile(targetPath))
      {
        var project = await workspace.OpenProjectAsync(targetPath).ConfigureAwait(false);
        solution = project.Solution;
      }
      else
      {
        throw new ArgumentException($"Path must be a .sln or project file (.csproj, .fsproj): {targetPath}");
      }
    }
    catch (InvalidOperationException ex)
    {
      throw new InvalidOperationException($"Failed to open {(FileTypes.IsAnySolutionFile(targetPath) ? "solution" : "project")}: {ex.Message}", ex);
    }
    catch (FileNotFoundException ex)
    {
      throw new FileNotFoundException($"Project or solution file not found: {ex.Message}", ex);
    }
    catch (IOException ex)
    {
      throw new IOException($"IO error while opening {(FileTypes.IsAnySolutionFile(targetPath) ? "solution" : "project")}: {ex.Message}", ex);
    }
    catch (UnauthorizedAccessException ex)
    {
      throw new UnauthorizedAccessException($"Access denied to {(FileTypes.IsAnySolutionFile(targetPath) ? "solution" : "project")}: {ex.Message}", ex);
    }

    var allDocuments = solution.Projects.SelectMany(project => project.Documents);
    var channel = Channel.CreateUnbounded<DiagnosticMessage>();
    var writer = channel.Writer;

    var parallelTask = Parallel.ForEachAsync(allDocuments,
      new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
      async (document, cancellationToken) =>
      {
        try
        {
          var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
          if (semanticModel == null) return;

          var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);

          foreach (var diagnostic in diagnostics)
          {
            if (!ShouldIncludeDiagnostic(diagnostic, includeWarnings))
              continue;

            var lineSpan = diagnostic.Location.GetLineSpan();
            var diagnosticMessage = new DiagnosticMessage(
              FilePath: diagnostic.Location.SourceTree?.FilePath ?? document.FilePath ?? string.Empty,
              Range: new(
                new(lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character),
                new(lineSpan.EndLinePosition.Line, lineSpan.EndLinePosition.Character)
              ),
              Severity: MapSeverity(diagnostic.Severity),
              Message: diagnostic.GetMessage(),
              Code: diagnostic.Id,
              Source: "roslyn",
              Category: diagnostic.Descriptor.Category,
              Project: document.Project.Name
            );

            await writer.WriteAsync(diagnosticMessage, cancellationToken).ConfigureAwait(false);
          }
        }
        catch (Exception ex)
        {
          logService.LogWarning("Failed to load document '{document.FilePath}': {ex.Message}", document.FilePath, ex.Message);
        }
      });

    // Close writer when parallel processing completes
    _ = parallelTask.ContinueWith(_ => writer.Complete(), TaskScheduler.Default);

    await foreach (var diagnostic in channel.Reader.ReadAllAsync())
    {
      yield return diagnostic;
    }
  }

  private static bool ShouldIncludeDiagnostic(Diagnostic diagnostic, bool includeWarnings) =>
    diagnostic.Severity == DiagnosticSeverity.Error ||
    (includeWarnings && diagnostic.Severity == DiagnosticSeverity.Warning);

  private static int MapSeverity(DiagnosticSeverity severity) => severity switch
  {
    DiagnosticSeverity.Error => 1,
    DiagnosticSeverity.Warning => 2,
    DiagnosticSeverity.Info => 3,
    DiagnosticSeverity.Hidden => 4,
    _ => 3
  };
}