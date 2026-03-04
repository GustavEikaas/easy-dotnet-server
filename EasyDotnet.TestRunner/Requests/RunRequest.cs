namespace EasyDotnet.TestRunner.Requests;

/// <summary>
/// Request to run a specific test node or a group of tests.
/// </summary>
/// <param name="NodeId">ID of the node to run. Could be project, namespace, class, or individual test.</param>
/// <param name="Configuration">Optional build/test configuration (e.g., Debug/Release).</param>
public sealed record RunRequest(
    string NodeId,
    string? Configuration = null
);
