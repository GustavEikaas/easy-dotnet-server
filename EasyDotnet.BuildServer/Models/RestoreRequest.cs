namespace EasyDotnet.BuildServer.Models;

public sealed record RestoreRequest(
    string ProjectFile,
    Dictionary<string, string>? Properties = null
);
