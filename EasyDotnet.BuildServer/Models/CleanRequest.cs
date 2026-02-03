namespace EasyDotnet.BuildServer.Models;

public sealed record CleanRequest(
    string ProjectFile,
    string? Configuration,
    Dictionary<string, string>? Properties = null
);
