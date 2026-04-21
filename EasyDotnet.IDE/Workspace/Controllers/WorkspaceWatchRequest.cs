namespace EasyDotnet.IDE.Workspace.Controllers;

public sealed record WorkspaceWatchRequest(
    bool UseDefault,
    bool UseLaunchProfile,
    bool UseDebugger,
    string? FilePath,
    string? CliArgs
);