namespace EasyDotnet.IDE.Workspace.Controllers;

public sealed record WorkspaceBuildRequest(
    bool UseDefault,
    bool UseTerminal,
    string? BuildArgs
);