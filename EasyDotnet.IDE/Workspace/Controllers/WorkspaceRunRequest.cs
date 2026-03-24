namespace EasyDotnet.IDE.Workspace.Controllers;

public sealed record WorkspaceRunRequest(
    bool UseDefault,
    bool UseLaunchProfile,
    string? FilePath,
    string? CliArgs
);