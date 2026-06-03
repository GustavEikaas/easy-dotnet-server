namespace EasyDotnet.BuildServer.Contracts;

public sealed record BatchBuildRequest(
    string[] ProjectPaths,
    string? Configuration,
    string? Platform = null,
    string? TargetFramework = null,
    string? BuildTarget = "Build",
    bool RestoreBeforeBuild = false
);

public enum BatchBuildResultKind { Started, Finished }

public sealed record BatchBuildResult(
    string ProjectPath,
    BatchBuildResultKind Kind,
    bool? Success,         // null when Kind = Started
    string? ErrorMessage,
    BatchBuildOutput? Output
);

public sealed record BatchBuildOutput(
    TimeSpan Duration,
    BuildDiagnostic[] Diagnostics
);