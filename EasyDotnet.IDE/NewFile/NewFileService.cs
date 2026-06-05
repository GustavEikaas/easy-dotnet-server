using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Extensions;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Picker.Models;
using EasyDotnet.IDE.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using StreamJsonRpc;

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

    return await editorService.ApplyWorkspaceEdit(BuildEdit(filePath, unit.ToFullString()));
  }

  public async Task CreateItem(string outputPath, bool preferFileScopedNamespace, CancellationToken cancellationToken)
  {
    if (!Path.IsPathFullyQualified(outputPath))
    {
      throw new ArgumentException($"Output path must be absolute: {outputPath}", nameof(outputPath));
    }

    if (FindCsprojFromDirectory(outputPath) is null)
    {
      await editorService.DisplayError($"No .csproj file found for {outputPath}");
      return;
    }

    var selection = await editorService.RequestPickerAsync(
        "Type",
        [
          new PickerChoice<TypeKindChoice>("enum", "Enum", new TypeKindChoice(Kind.Enum)),
          new PickerChoice<TypeKindChoice>("record", "Record", new TypeKindChoice(Kind.Record)),
          new PickerChoice<TypeKindChoice>("interface", "Interface", new TypeKindChoice(Kind.Interface)),
          new PickerChoice<TypeKindChoice>("class", "Class", new TypeKindChoice(Kind.Class))
        ],
        ct: cancellationToken);

    if (selection is null)
      return;

    string? typeName;
    try
    {
      typeName = await editorService.RequestString("Type name:", null);
    }
    catch (RemoteInvocationException e) when (e.Message.Contains("User aborted", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    typeName = typeName?.Trim();
    if (!IsValidTypeName(typeName))
    {
      await editorService.DisplayWarning($"Invalid C# type name: {typeName}");
      return;
    }

    Directory.CreateDirectory(outputPath);
    var filePath = Path.Combine(outputPath, $"{typeName}.cs");

    if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
    {
      await editorService.DisplayWarning($"File already exists and is not empty: {filePath}");
      return;
    }

    var success = await BootstrapFile(filePath, selection.Kind, preferFileScopedNamespace, cancellationToken);
    if (success)
    {
      await editorService.RequestOpenBuffer(filePath);
    }
    else
    {
      await editorService.DisplayError("Failed to create new file");
    }
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
    return dir == null ? null : FindCsprojFromDirectory(dir);
  }

  private static string? FindCsprojFromDirectory(string directory)
  {
    var current = directory;
    while (!Directory.Exists(current))
    {
      var parent = Directory.GetParent(current);
      if (parent is null)
        return null;

      current = parent.FullName;
    }

    return FindCsprojInDirectoryOrParents(current);
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

      Kind.Enum => SyntaxFactory.EnumDeclaration(className)
          .WithModifiers(modifiers)
          .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
          .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)),

      _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
  }

  private static bool IsValidTypeName(string? typeName) =>
      typeName is not null && SyntaxFacts.IsValidIdentifier(typeName);

  private sealed record TypeKindChoice(Kind Kind);
}