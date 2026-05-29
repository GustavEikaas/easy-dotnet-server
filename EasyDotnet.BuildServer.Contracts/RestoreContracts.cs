namespace EasyDotnet.BuildServer.Contracts;

public sealed record RestoreRequest(
    string[] ProjectPaths,
    string? Configuration = null,
    string? Platform = null);

public sealed record RestoreResponse(RestoreResult[] Results);

public sealed record RestoreResult(
    string ProjectPath,
    bool Success,
    string? ErrorMessage,
    RestoreOutput? Output);

public sealed record RestoreOutput(
    TimeSpan Duration,
    BuildDiagnostic[] Diagnostics,
    bool NoOp = false,
    string? NoOpReason = null);