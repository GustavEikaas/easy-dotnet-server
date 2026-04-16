using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace.Test;

/// <summary>
/// Typed wrappers for workspace/test RPC calls.
/// Eliminates the magic method string and anonymous-object parameter shape from test code.
/// </summary>
public static class WorkspaceTestExtensions
{
  public static Task WorkspaceTestAsync(
    this JsonRpc rpc,
    bool useDefault = false,
    string? testArgs = null)
    => rpc.InvokeWithParameterObjectAsync("workspace/test",
      new { useDefault, testArgs });

  public static Task WorkspaceTestSolutionAsync(
    this JsonRpc rpc,
    bool useDefault = false,
    string? testArgs = null)
    => rpc.InvokeWithParameterObjectAsync("workspace/test-solution",
      new { useDefault, testArgs });
}
