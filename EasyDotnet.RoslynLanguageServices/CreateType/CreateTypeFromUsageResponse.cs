namespace EasyDotnet.RoslynLanguageServices.CreateType;

public sealed record CreateTypeFromUsageResponse(
    bool CanCreate,
    string? TypeName,
    string? FilePath,
    string? FileText,
    string? Title,
    string? Reason)
{
  public static CreateTypeFromUsageResponse No(string reason) =>
      new(false, null, null, null, null, reason);

  public static CreateTypeFromUsageResponse Yes(string typeName, string filePath, string fileText) =>
      new(true, typeName, filePath, fileText, $"Create class '{typeName}' in {typeName}.cs", "Unresolved type usage.");
}
