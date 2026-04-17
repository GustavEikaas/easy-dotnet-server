using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace.Build;

/// <summary>
/// Typed wrappers for workspace/build RPC calls.
/// Eliminates the magic method string and anonymous-object parameter shape from test code.
/// </summary>
public static class WorkspaceBuildExtensions
{
  public static Task WorkspaceBuildAsync(
    this JsonRpc rpc,
    bool useDefault = false,
    bool useTerminal = false,
    string? buildArgs = null)
    => rpc.InvokeWithParameterObjectAsync("workspace/build",
      new { useDefault, useTerminal, buildArgs });

  public static Task WorkspaceBuildSolutionAsync(
    this JsonRpc rpc,
    bool useDefault = false,
    bool useTerminal = false,
    string? buildArgs = null)
    => rpc.InvokeWithParameterObjectAsync("workspace/build-solution",
      new { useDefault, useTerminal, buildArgs });
}