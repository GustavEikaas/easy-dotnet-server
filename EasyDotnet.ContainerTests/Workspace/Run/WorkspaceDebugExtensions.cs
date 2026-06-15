using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Typed wrapper for the workspace/debug RPC call.
/// Shares the request shape with workspace/run.
/// </summary>
public static class WorkspaceDebugExtensions
{
  public static Task WorkspaceDebugAsync(
    this JsonRpc rpc,
    bool useDefault = false,
    bool useLaunchProfile = false,
    string? filePath = null,
    string? cliArgs = null)
    => rpc.InvokeWithParameterObjectAsync("workspace/debug",
      new { useDefault, useLaunchProfile, filePath, cliArgs });
}