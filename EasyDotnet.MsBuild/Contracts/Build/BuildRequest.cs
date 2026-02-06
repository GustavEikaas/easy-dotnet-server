namespace EasyDotnet.MsBuild.Contracts.Build;

public sealed record BuildRequest(
    string ProjectFile,
    string? Configuration,
    Dictionary<string, string>? Properties = null
);
