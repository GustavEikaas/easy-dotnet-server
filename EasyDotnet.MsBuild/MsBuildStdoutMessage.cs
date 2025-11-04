namespace EasyDotnet.MsBuild;

public sealed record MsBuildStdoutMessage(string Type, string FilePath, int LineNumber, int ColumnNumber, string Code, string? Message);