using System.Collections.Generic;

namespace EasyDotnet.IDE.BuildServerContracts;

public record BuildServerRequest(string ProjectFile, string Configuration);

public record BuildServerResult(
    bool Success,
    List<BuildMessageDto> Errors,
    List<BuildMessageDto> Warnings,
    string? Fun
);

public record BuildMessageDto(
    string Type, string FilePath, int LineNumber, int ColumnNumber,
    string Code, string Message, string? ProjectFile
);