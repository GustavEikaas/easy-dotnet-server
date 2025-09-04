namespace EasyDotnet.Controllers.Roslyn;

public sealed record DiagnosticPosition(int Line, int Character);
public sealed record DiagnosticRange(DiagnosticPosition Start, DiagnosticPosition End);
public sealed record DiagnosticMessage(
    string FilePath,
    DiagnosticRange Range,
    int Severity,
    string Message,
    string Code,
    string Source = "roslyn",
    string? Category = null
);