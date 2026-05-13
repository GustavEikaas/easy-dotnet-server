using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace.Clean;

public static class WorkspaceCleanExtensions
{
  public static Task WorkspaceCleanAsync(
    this JsonRpc rpc)
    => rpc.InvokeWithParameterObjectAsync("workspace/clean",
      new { _ = "" });
}
