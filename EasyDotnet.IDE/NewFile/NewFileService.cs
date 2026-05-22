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
      return false;

    var context = await ResolveProjectContext(filePath, cancellationToken);
    var templateKind = ResolveTemplateKind(filePath);

    var content = templateKind switch
    {
      NewFileTemplateKind.CSharp =>
        BuildCSharpContent(filePath, kind, context, preferFileScopedNamespace),

      NewFileTemplateKind.RazorComponent =>
        BuildRazorComponentContent(filePath),

      NewFileTemplateKind.RazorComponentCodeBehind =>
        BuildRazorCodeBehindContent(filePath, context, preferFileScopedNamespace),

      NewFileTemplateKind.RazorPageOrView =>
        BuildCshtmlContent(filePath, context),

      NewFileTemplateKind.RazorPageModel =>
        BuildCshtmlCodeBehindContent(filePath, context, preferFileScopedNamespace),

      _ => throw new ArgumentOutOfRangeException(nameof(templateKind))
    };

    await editorService.ApplyWorkspaceEdit(BuildEdit(filePath, content));
    return true;
  }

  // Each entry covers a suffix, its template kind, and whether the logical type name gets a "Model" suffix appended.
  // Ordered so compound extensions (.razor.cs, .cshtml.cs) are matched before their plain counterparts.
  private sealed record FileKindEntry(string Suffix, NewFileTemplateKind Kind, bool AppendModelSuffix = false);

  private static readonly FileKindEntry[] s_fileKindRules =
  [
    new(".razor.cs",  NewFileTemplateKind.RazorComponentCodeBehind),
    new(".cshtml.cs", NewFileTemplateKind.RazorPageModel, AppendModelSuffix: true),
    new(".razor",     NewFileTemplateKind.RazorComponent),
    new(".cshtml",    NewFileTemplateKind.RazorPageOrView),
  ];

  private static NewFileTemplateKind ResolveTemplateKind(string filePath)
  {
    var fileName = Path.GetFileName(filePath);
    return s_fileKindRules.FirstOrDefault(r => fileName.EndsWith(r.Suffix, StringComparison.OrdinalIgnoreCase))?.Kind
        ?? NewFileTemplateKind.CSharp;
  }

  private static string GetLogicalTypeName(string filePath)
  {
    var fileName = Path.GetFileName(filePath);
    var rule = s_fileKindRules.FirstOrDefault(r => fileName.EndsWith(r.Suffix, StringComparison.OrdinalIgnoreCase));

    if (rule is not null)
    {
      var baseName = fileName[..^rule.Suffix.Length];
      return rule.AppendModelSuffix ? baseName + "Model" : baseName;
    }

    // Plain .cs: strip extension, take first dot-segment (handles e.g. Foo.generated.cs => Foo)
    return fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        ? fileName[..^".cs".Length].Split('.').First()
        : Path.GetFileNameWithoutExtension(filePath);
  }

  private static string BuildCSharpContent(
    string filePath,
    Kind kind,
    (string rootNamespace, string? langVersion, string relativeDir) context,
    bool preferFileScopedNamespace)
  {
    var (rootNamespace, langVersion, relativeDir) = context;

    var supportsFileScopedNamespace =
        string.IsNullOrEmpty(langVersion) ||
        string.Compare(langVersion, "10.0", StringComparison.OrdinalIgnoreCase) >= 0 ||
        langVersion.Equals("latest", StringComparison.OrdinalIgnoreCase);

    var useFileScopedNs = preferFileScopedNamespace && supportsFileScopedNamespace;

    var nsSuffix = relativeDir.Replace(Path.DirectorySeparatorChar, '.');
    var fullNamespace = string.IsNullOrEmpty(nsSuffix) ? rootNamespace : $"{rootNamespace}.{nsSuffix}";

    var className = GetLogicalTypeName(filePath);
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
      unit = unit.AddNewLinesAfterNamespaceDeclaration();

    return unit.ToFullString();
  }

  private static string BuildRazorComponentContent(string filePath)
  {
    var name = GetLogicalTypeName(filePath);
    return $"<h3>{name}</h3>\n";
  }

  private static string BuildRazorCodeBehindContent(
    string filePath,
    (string rootNamespace, string? langVersion, string relativeDir) context,
    bool preferFileScopedNamespace)
  {
    var (rootNamespace, langVersion, relativeDir) = context;

    var supportsFileScopedNamespace =
        string.IsNullOrEmpty(langVersion) ||
        string.Compare(langVersion, "10.0", StringComparison.OrdinalIgnoreCase) >= 0 ||
        langVersion.Equals("latest", StringComparison.OrdinalIgnoreCase);

    var useFileScopedNs = preferFileScopedNamespace && supportsFileScopedNamespace;

    var nsSuffix = relativeDir.Replace(Path.DirectorySeparatorChar, '.');
    var fullNamespace = string.IsNullOrEmpty(nsSuffix) ? rootNamespace : $"{rootNamespace}.{nsSuffix}";

    var className = GetLogicalTypeName(filePath);
    var typeDecl = SyntaxFactory.ClassDeclaration(className)
        .WithModifiers(SyntaxFactory.TokenList(
          SyntaxFactory.Token(SyntaxKind.PublicKeyword),
          SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
        .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
        .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken));

    BaseNamespaceDeclarationSyntax nsDeclaration = useFileScopedNs
        ? SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
              .AddMembers(typeDecl)
        : SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
              .AddMembers(typeDecl);

    var unit = SyntaxFactory.CompilationUnit()
        .AddMembers(nsDeclaration)
        .NormalizeWhitespace(eol: "\n");

    if (preferFileScopedNamespace)
      unit = unit.AddNewLinesAfterNamespaceDeclaration();

    return unit.ToFullString();
  }

  private static string BuildCshtmlContent(
    string filePath,
    (string rootNamespace, string? langVersion, string relativeDir) context)
  {
    var name = GetLogicalTypeName(filePath);
    var relativeDir = context.relativeDir;

    if (relativeDir.StartsWith("Pages", StringComparison.OrdinalIgnoreCase))
    {
      return $"@page\n@model {name}Model\n\n<h1>{name}</h1>\n";
    }

    if (relativeDir.StartsWith("Views", StringComparison.OrdinalIgnoreCase))
    {
      return $"@{{\n    ViewData[\"Title\"] = \"{name}\";\n}}\n\n<h1>{name}</h1>\n";
    }

    return $"@{{\n}}\n\n<h1>{name}</h1>\n";
  }

  private static string BuildCshtmlCodeBehindContent(
    string filePath,
    (string rootNamespace, string? langVersion, string relativeDir) context,
    bool preferFileScopedNamespace)
  {
    var (rootNamespace, langVersion, relativeDir) = context;

    var supportsFileScopedNamespace =
        string.IsNullOrEmpty(langVersion) ||
        string.Compare(langVersion, "10.0", StringComparison.OrdinalIgnoreCase) >= 0 ||
        langVersion.Equals("latest", StringComparison.OrdinalIgnoreCase);

    var useFileScopedNs = preferFileScopedNamespace && supportsFileScopedNamespace;

    var nsSuffix = relativeDir.Replace(Path.DirectorySeparatorChar, '.');
    var fullNamespace = string.IsNullOrEmpty(nsSuffix) ? rootNamespace : $"{rootNamespace}.{nsSuffix}";

    var className = GetLogicalTypeName(filePath);

    var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName("PageModel"));
    var typeDecl = SyntaxFactory.ClassDeclaration(className)
        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
        .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType)))
        .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
        .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken));

    BaseNamespaceDeclarationSyntax nsDeclaration = useFileScopedNs
        ? SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
              .AddMembers(typeDecl)
        : SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(fullNamespace))
              .AddMembers(typeDecl);

    var unit = SyntaxFactory.CompilationUnit()
        .AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Microsoft.AspNetCore.Mvc.RazorPages")))
        .AddMembers(nsDeclaration)
        .NormalizeWhitespace(eol: "\n");

    if (preferFileScopedNamespace)
      unit = unit.AddNewLinesAfterNamespaceDeclaration();

    return unit.ToFullString();
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