namespace EasyDotnet.BuildServer.Models;

public sealed record PublishRequest(
    string ProjectFile,
    string? Configuration,
    Dictionary<string, string>? Properties = null
);
