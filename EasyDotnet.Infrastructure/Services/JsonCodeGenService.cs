using EasyDotnet.Application.Interfaces;
using EasyDotnet.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis.MSBuild;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;

namespace EasyDotnet.Infrastructure.Services;

public class JsonCodeGenService(IMsBuildService msBuildService) : IJsonCodeGenService
{
  public async Task<string> ConvertJsonToCSharpCompilationUnit(string jsonData, string filePath, bool preferFileScopedNamespace)
  {
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

    var className = Path.GetFileNameWithoutExtension(filePath).Split(".").ElementAt(0)!;
    var code = generator.GenerateFile(className);

    var projectPath = FindCsprojFromFile(filePath);

    var project = await msBuildService.GetOrSetProjectPropertiesAsync(projectPath);
    var rootNamespace = project.RootNamespace;

    var relativePath = Path.GetDirectoryName(filePath)!
        .Replace(Path.GetDirectoryName(projectPath)!, "")
        .Trim(Path.DirectorySeparatorChar);
    var nsSuffix = relativePath.Replace(Path.DirectorySeparatorChar, '.');
    var fullNamespace = string.IsNullOrEmpty(nsSuffix) ? rootNamespace : $"{rootNamespace}.{nsSuffix}";

    var cleanClassesOnly = NJsonClassExtractor.ExtractClassesWithNamespace(code, fullNamespace!, preferFileScopedNamespace);

    return cleanClassesOnly;
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
}
