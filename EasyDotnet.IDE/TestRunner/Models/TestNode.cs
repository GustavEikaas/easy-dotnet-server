namespace EasyDotnet.IDE.TestRunner.Models;

/// <summary>
/// Flat node sent to the Lua client via registerTest notifications.
/// The client reconstructs the tree using Id/ParentId.
/// Native framework IDs (VSTest GUIDs, MTP Uids) are never included here.
/// </summary>
public record TestNode(
    string Id,
    string DisplayName,
    string? ParentId,
    string? FilePath,
    int? LineNumber,
    NodeType Type,
    string? ProjectId,
    List<TestAction> AvailableActions,
    string? TargetFramework = null
);