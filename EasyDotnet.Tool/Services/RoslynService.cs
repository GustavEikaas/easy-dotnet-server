using System;
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