using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace.Nuget;

/// <summary>
/// Typed wrappers for nuget/pack and nuget/pack-and-push RPC calls.
/// </summary>
public static class WorkspaceNugetExtensions
{
  public static Task WorkspacePackAsync(this JsonRpc rpc, string? filePath = null)
    => rpc.InvokeWithParameterObjectAsync("workspace/pack", new { filePath });

  public static Task WorkspacePackAndPushAsync(this JsonRpc rpc, string? filePath = null)
    => rpc.InvokeWithParameterObjectAsync("workspace/pack-and-push", new { filePath });
}