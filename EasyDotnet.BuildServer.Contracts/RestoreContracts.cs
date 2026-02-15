namespace EasyDotnet.BuildServer.Contracts;

public sealed record RestoreRequest(string[] ProjectPaths);

public sealed record RestoreResponse(RestoreResult[] Results);

public sealed record RestoreResult(
    string ProjectPath,
    bool Success,
    string? ErrorMessage,
    RestoreOutput? Output);

public sealed record RestoreOutput(
    TimeSpan Duration,
    BuildDiagnostic[] Diagnostics);