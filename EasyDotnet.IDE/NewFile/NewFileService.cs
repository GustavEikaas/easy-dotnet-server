using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Extensions;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;

namespace EasyDotnet.IDE.NewFile;

public class NewFileService(IBuildHostManager buildHostManager, IEditorService editorService)
{
  public async Task<bool> BootstrapFile(string filePath, Kind kind, bool preferFileScopedNamespace, CancellationToken cancellationToken)
  {
    if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
    {
      return false;
    }

    var (rootNamespace, langVersion, relativeDir) = await ResolveProjectContext(filePath, cancellationToken);

    var supportsFileScopedNamespace =
        string.IsNullOrEmpty(langVersion) ||
        string.Compare(langVersion, "10.0", StringComparison.OrdinalIgnoreCase) >= 0 ||
        langVersion.Equals("latest", StringComparison.OrdinalIgnoreCase);

    var useFileScopedNs = preferFileScopedNamespace && supportsFileScopedNamespace;

    var nsSuffix = relativeDir.Replace(Path.DirectorySeparatorChar, '.');
    var fullNamespace = string.IsNullOrEmpty(nsSuffix) ? rootNamespace : $"{rootNamespace}.{nsSuffix}";

    var className = Path.GetFileNameWithoutExtension(filePath).Split(".").ElementAt(0);
    var typeDecl = CreateTypeDeclaration(kind, className);

    BaseNamespaceDeclarationSyntax nsDeclaration = useFileScopedNs
        ? SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
              .AddMembers(typeDecl)
        : SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
              .AddMembers(typeDecl);

    var unit = SyntaxFactory.CompilationUnit()
        .AddMembers(nsDeclaration)
        .NormalizeWhitespace(eol: "\n");

    if (preferFileScopedNamespace)
    {
      unit = unit.AddNewLinesAfterNamespaceDeclaration();
    }

    await editorService.ApplyWorkspaceEdit(BuildEdit(filePath, unit.ToFullString()));
    return true;
  }

  public async Task<bool> BootstrapFileFromJson(string jsonData, string filePath, bool preferFileScopedNamespace, CancellationToken cancellationToken)
  {
    if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
    {
      return false;
    }

    var schema = JsonSchema.FromSampleJson(jsonData);
    var generator = new CSharpGenerator(schema, new CSharpGeneratorSettings
    {
      GenerateDataAnnotations = false,
      GenerateJsonMethods = false,
      JsonLibrary = CSharpJsonLibrary.SystemTextJson,
      GenerateOptionalPropertiesAsNullable = false,
      GenerateNullableReferenceTypes = false,
      ClassStyle = CSharpClassStyle.Poco,
      GenerateDefaultValues = false,
      HandleReferences = false,
      RequiredPropertiesMustBeDefined = false
    });
    schema.AllowAdditionalProperties = false;

    var className = Path.GetFileNameWithoutExtension(filePath).Split(".").ElementAt(0);
    var code = generator.GenerateFile(className);

    var (rootNamespace, _, relativeDir) = await ResolveProjectContext(filePath, cancellationToken);

    var nsSuffix = relativeDir.Replace(Path.DirectorySeparatorChar, '.');
    var fullNamespace = string.IsNullOrEmpty(nsSuffix) ? rootNamespace : $"{rootNamespace}.{nsSuffix}";

    var content = NJsonClassExtractor.ExtractClassesWithNamespace(code, fullNamespace, preferFileScopedNamespace);

    await editorService.ApplyWorkspaceEdit(BuildEdit(filePath, content));
    return true;
  }

  private async Task<(string rootNamespace, string? langVersion, string relativeDir)> ResolveProjectContext(string filePath, CancellationToken cancellationToken)
  {
    var projectPath = FindCsprojFromFile(filePath);

    if (projectPath != null)
    {
      var evalResult = await buildHostManager
          .GetProjectPropertiesBatchAsync(new GetProjectPropertiesBatchRequest([projectPath], null), cancellationToken)
          .FirstOrDefaultAsync(cancellationToken);

      var raw = evalResult?.Project?.Raw;
      var rootNamespace = raw?.RootNamespace ?? raw?.MSBuildProjectName ?? Path.GetFileNameWithoutExtension(projectPath);
      var langVersion = raw?.LangVersion;
      var relativeDir = Path.GetDirectoryName(filePath)!
          .Replace(Path.GetDirectoryName(projectPath)!, "")
          .Trim(Path.DirectorySeparatorChar);

      return (rootNamespace, langVersion, relativeDir);
    }

    return (Path.GetFileNameWithoutExtension(filePath).Split(".").First(), null, string.Empty);
  }

  private static WorkspaceEdit BuildEdit(string filePath, string content) =>
    new([
      new WorkspaceDocumentChange(
        new TextDocumentIdentifier($"file://{filePath}"),
        [new TextEdit(new TextEditRange(new TextEditPosition(0, 0), new TextEditPosition(0, 0)), content)]
      )
    ]);

  private static string? FindCsprojFromFile(string filePath)
  {
    var dir = Path.GetDirectoryName(filePath);
    return dir == null ? null : FindCsprojInDirectoryOrParents(dir);
  }

  private static string? FindCsprojInDirectoryOrParents(string directory)
  {
    var csproj = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
    if (csproj != null)
    {
      return csproj;
    }

    var parent = Directory.GetParent(directory);
    return parent != null ? FindCsprojInDirectoryOrParents(parent.FullName) : null;
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