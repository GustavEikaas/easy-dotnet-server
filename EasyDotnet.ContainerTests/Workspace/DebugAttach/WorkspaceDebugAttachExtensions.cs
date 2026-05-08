using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace.DebugAttach;

/// <summary>
/// Typed wrapper for workspace/debug-attach RPC calls.
/// </summary>
public static class WorkspaceDebugAttachExtensions
{
  public static Task WorkspaceDebugAttachAsync(this JsonRpc rpc) =>
    rpc.InvokeWithParameterObjectAsync("workspace/debug-attach", new { });
}