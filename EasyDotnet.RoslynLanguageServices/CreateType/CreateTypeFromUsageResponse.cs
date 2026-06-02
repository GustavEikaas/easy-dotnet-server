using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.CreateType;

public sealed record CreateTypeFromUsageResponse(
    [property: JsonPropertyName("canCreate")] bool CanCreate,
    [property: JsonPropertyName("typeName")] string? TypeName,
    [property: JsonPropertyName("filePath")] string? FilePath,
    [property: JsonPropertyName("fileText")] string? FileText,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("reason")] string? Reason)
{
  public static CreateTypeFromUsageResponse No(string reason) =>
      new(false, null, null, null, null, reason);

  public static CreateTypeFromUsageResponse Yes(string typeName, string filePath, string fileText) =>
      new(true, typeName, filePath, fileText, $"Create class '{typeName}' in {typeName}.cs", "Unresolved type usage.");
}
