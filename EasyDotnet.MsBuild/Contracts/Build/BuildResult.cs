namespace EasyDotnet.MsBuild.Contracts.Build;

public sealed record BuildRpcResult(
    bool Success,
    List<BuildMessage> Errors,
    List<BuildMessage> Warnings,
    string? Fun
);

public sealed record BuildMessage(
    string Type,
    string FilePath,
    int LineNumber,
    int ColumnNumber,
    string Code,
    string Message,
    string? ProjectFile
);