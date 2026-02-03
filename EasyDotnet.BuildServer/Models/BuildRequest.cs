namespace EasyDotnet.BuildServer.Models;

public sealed record BuildRequest(
    string ProjectFile,
    string? Configuration,
    Dictionary<string, string>? Properties = null
);