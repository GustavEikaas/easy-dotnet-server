using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.Diagnostics;

public sealed record WorkspaceDiagnosticsResponse(
    [property: JsonPropertyName("projectCount")] int ProjectCount,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<WorkspaceDiagnostic> Diagnostics);

public sealed record WorkspaceDiagnostic(
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("range")] WorkspaceDiagnosticRange Range,
    [property: JsonPropertyName("severity")] int Severity,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("project")] string Project);

public sealed record WorkspaceDiagnosticRange(
    [property: JsonPropertyName("start")] WorkspaceDiagnosticPosition Start,
    [property: JsonPropertyName("end")] WorkspaceDiagnosticPosition End);

public sealed record WorkspaceDiagnosticPosition(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);