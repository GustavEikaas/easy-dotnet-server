using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace.Nuget;

/// <summary>
/// Typed wrappers for nuget/pack and nuget/pack-and-push RPC calls.
/// </summary>
public static class WorkspaceNugetExtensions
{
  public static Task NugetPackAsync(this JsonRpc rpc, string? filePath = null)
    => rpc.InvokeWithParameterObjectAsync("nuget/pack", new { filePath });

  public static Task NugetPackAndPushAsync(this JsonRpc rpc, string? filePath = null)
    => rpc.InvokeWithParameterObjectAsync("nuget/pack-and-push", new { filePath });
}