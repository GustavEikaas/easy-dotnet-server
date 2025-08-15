using System.IO.Pipes;
using EasyDotnet.MsBuild.Contracts;
using EasyDotnet.MsBuildSdk.Controllers;
using Microsoft.Build.Locator;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.MsBuildSdk;


class Program
{
  static SdkInstallation[] BootstrapMsBuild()
  {
    MSBuildLocator.AllowQueryAllRuntimeVersions = true;
    var instances = MSBuildLocator.QueryVisualStudioInstances().Where(x => x.DiscoveryType == DiscoveryType.DotNetSdk).ToList();
    var monikers = instances.Select(x => new SdkInstallation(x.Name, $"net{x.Version.Major}.0", x.Version, x.MSBuildPath, x.VisualStudioRootPath)).ToArray();
    MSBuildLocator.RegisterDefaults();
    return monikers;
  }

  static async Task Main(string[] args)
  {
    var monikers = BootstrapMsBuild();
    var pipe = args[0];
    await StartServerAsync(pipe, monikers);
  }

  private static async Task StartServerAsync(string pipeName, SdkInstallation[] monikers)
  {
    var clientId = 0;
    while (true)
    {
      var stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
      Console.WriteLine($"Named pipe server started: {pipeName}");
      await stream.WaitForConnectionAsync();
      _ = RespondToRpcRequestsAsync(stream, ++clientId, monikers);
    }
  }

  private static async Task RespondToRpcRequestsAsync(Stream stream, int clientId, SdkInstallation[] monikers)
  {
    var jsonMessageFormatter = new JsonMessageFormatter();
    jsonMessageFormatter.JsonSerializer.ContractResolver = new DefaultContractResolver
    {
      NamingStrategy = new CamelCaseNamingStrategy(),
    };

    var handler = new HeaderDelimitedMessageHandler(stream, stream, jsonMessageFormatter);
    var jsonRpc = new JsonRpc(handler);
    jsonRpc.AddLocalRpcTarget(new MsbuildController(monikers));

    jsonRpc.StartListening();
    Console.WriteLine($"JSON-RPC listener attached to #{clientId}. Waiting for requests...");
    await jsonRpc.Completion;
    await Console.Error.WriteLineAsync($"Connection #{clientId} terminated.");
  }
}