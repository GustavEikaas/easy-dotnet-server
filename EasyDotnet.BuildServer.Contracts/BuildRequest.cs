namespace EasyDotnet.BuildServer.Contracts;

public sealed record BuildRequest(
    string ProjectFile,
    string? Configuration,
    Dictionary<string, string>? Properties = null
);