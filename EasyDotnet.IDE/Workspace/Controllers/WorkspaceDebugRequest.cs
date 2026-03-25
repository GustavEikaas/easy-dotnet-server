namespace EasyDotnet.IDE.Workspace.Controllers;

public sealed record WorkspaceDebugRequest(
    bool UseDefault,
    bool UseLaunchProfile,
    string? FilePath
);
