namespace EasyDotnet.IDE.Workspace.Controllers;

public sealed record WorkspaceTestRequest(
    bool UseDefault,
    string? TestArgs
);
