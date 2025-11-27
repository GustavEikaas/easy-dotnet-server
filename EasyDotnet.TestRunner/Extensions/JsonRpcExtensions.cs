using EasyDotnet.TestRunner.Services;
using Microsoft.Extensions.DependencyInjection;
using StreamJsonRpc;

namespace EasyDotnet.TestRunner.Extensions;

public sealed record NoOpResponse(bool Success = true);

public static class JsonRpcExtensions
{
  public static JsonRpc MapTestRunnerEndpoints(this JsonRpc rpcServer, IServiceProvider serviceProvider)
  {
    var runner = serviceProvider.GetRequiredService<ITestRunner>();

    rpcServer.AddLocalRpcMethod("testrunner/initialize", async (string solutionFilePath, CancellationToken ct) =>
    {
      await runner.InitializeAsync(solutionFilePath, ct);
      return new NoOpResponse();
    });

    rpcServer.AddLocalRpcMethod("testrunner/discover", async (CancellationToken ct) =>
    {
      await runner.StartDiscoveryAsync(ct);
      return new NoOpResponse();
    });

    return rpcServer;
  }
}