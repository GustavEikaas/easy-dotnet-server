using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer;

public static class LanguageServer
{
  public static async Task Start(Stream writer, Stream reader, CancellationToken cancellationToken)
  {
    var formatter = CreateJsonMessageFormatter();
    var handler = new HeaderDelimitedMessageHandler(writer, reader, formatter);
    var jsonRpc = new JsonRpc(handler);
    var di = DiModules.BuildServiceProvider(jsonRpc);

    RegisterControllers(jsonRpc, di);
    jsonRpc.StartListening();
    await jsonRpc.Completion;
  }

  private static JsonMessageFormatter CreateJsonMessageFormatter() => new()
  {
    JsonSerializer = { ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }}
  };

  private static void RegisterControllers(JsonRpc jsonRpc, IServiceProvider provider) => AssemblyScanner.GetControllerTypes().ForEach(x => jsonRpc.AddLocalRpcTarget(provider.GetRequiredService(x)));
}