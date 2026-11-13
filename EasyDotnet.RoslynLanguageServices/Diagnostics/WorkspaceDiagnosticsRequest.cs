using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.Diagnostics;

public sealed record WorkspaceDiagnosticsRequest(
    [property: JsonPropertyName("targetPath")] string? TargetPath,
    [property: JsonPropertyName("includeWarnings")] bool IncludeWarnings);