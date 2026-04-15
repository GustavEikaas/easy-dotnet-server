using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Typed wrappers for workspace/run RPC calls.
/// Eliminates the magic method string and anonymous-object parameter shape from test code.
/// </summary>
public static class WorkspaceRunExtensions
{
  public static Task WorkspaceRunAsync(
    this JsonRpc rpc,
    bool useDefault = false,
    bool useLaunchProfile = false,
    string? filePath = null,
    string? cliArgs = null)
    => rpc.InvokeWithParameterObjectAsync("workspace/run",
      new { useDefault, useLaunchProfile, filePath, cliArgs });
}