namespace EasyDotnet.TestRunner.Requests;

/// <summary>
/// Request to debug a specific test node or group of tests.
/// </summary>
/// <param name="NodeId">ID of the node to debug.</param>
/// <param name="Configuration">Optional build/debug configuration.</param>
public sealed record DebugRequest(
    string NodeId,
    string? Configuration = null
);