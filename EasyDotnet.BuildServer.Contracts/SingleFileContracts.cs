namespace EasyDotnet.BuildServer.Contracts;

/// <summary>
/// Request to convert a single .cs entry point file to a virtual project.
/// </summary>
public record ConvertSingleFileRequest(string EntryPointFilePath);

/// <summary>
/// Response containing the virtual project path and evaluation result.
/// </summary>
public record ConvertSingleFileResponse(
    string ProjectFilePath,
    ProjectEvaluationResult Properties
);