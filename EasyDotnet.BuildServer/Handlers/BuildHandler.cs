using EasyDotnet.BuildServer.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

/// <summary>
/// Handles RPC methods for build operations (build, restore, clean, publish)
/// </summary>
public class BuildHandler()
{
  /// <summary>
  /// RPC: build/execute
  /// Executes a build target (Build, Restore, Clean, Publish)
  /// </summary>
  /// <exception cref="NotImplementedException"></exception>
  [JsonRpcMethod("build/execute")]
  public async Task<BuildRpcResult> ExecuteBuildAsync(
      BuildRequest request,
      CancellationToken cancellationToken) => throw new NotImplementedException();


  [JsonRpcMethod("multi-build/execute")]
  public async Task<BuildRpcResult> ExecuteMultiBuildAsync(
      BuildRequest request,
      CancellationToken cancellationToken) => throw new NotImplementedException();
}