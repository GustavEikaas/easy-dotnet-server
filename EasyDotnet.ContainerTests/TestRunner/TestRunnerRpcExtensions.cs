using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// Typed wrappers around the <c>testrunner/*</c> RPC calls. Parallels
/// <see cref="EasyDotnet.ContainerTests.Workspace.Test.WorkspaceTestExtensions"/>.
/// </summary>
public static class TestRunnerRpcExtensions
{
  public static Task TestRunnerInitializeAsync(this JsonRpc rpc, string solutionPath) =>
    rpc.InvokeWithParameterObjectAsync("testrunner/initialize", new { solutionPath });

  public static Task TestRunnerRunAsync(this JsonRpc rpc, string id, string? source = null) =>
    rpc.InvokeWithParameterObjectAsync("testrunner/run", new { id, source });
}