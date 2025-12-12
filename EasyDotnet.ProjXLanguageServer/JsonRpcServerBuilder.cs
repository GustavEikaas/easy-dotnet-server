using System.Diagnostics;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer;

public static class JsonRpcServerBuilder
{
  public static JsonRpc Build(Stream writer, Stream reader, Func<JsonRpc, SourceLevels, ServiceProvider>? buildServiceProvider = null, SourceLevels? logLevel = SourceLevels.Off)
  {
    var formatter = CreateJsonMessageFormatter();
    var handler = new HeaderDelimitedMessageHandler(writer, reader, formatter);
    var jsonRpc = new JsonRpc(handler);

    var sp = buildServiceProvider is not null ? buildServiceProvider(jsonRpc, logLevel ?? SourceLevels.Off) : DiModules.BuildServiceProvider(jsonRpc);
    RegisterControllers(jsonRpc, sp);
    // #if DEBUG
    //     jsonRpc.TraceSource.Switch.Level = SourceLevels.Verbose;
    // #endif
    return jsonRpc;
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