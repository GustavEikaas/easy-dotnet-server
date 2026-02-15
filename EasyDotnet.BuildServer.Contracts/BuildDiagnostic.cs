namespace EasyDotnet.BuildServer.Contracts;

public sealed record BuildDiagnostic(
    string? File,
    int LineNumber,
    int ColumnNumber,
    int EndLineNumber,
    int EndColumnNumber,
    string? Message,
    string? Code,
    string? ProjectFile,
    BuildDiagnosticSeverity Severity);

public enum BuildDiagnosticSeverity
{
  Message = 0,
  Warning = 1,
  Error = 2
}