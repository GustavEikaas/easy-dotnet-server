using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Extensions.DependencyInjection;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer;

public static class DiModules
{
  public static ServiceProvider BuildServiceProvider(JsonRpc jsonRpc)
  {
    var services = new ServiceCollection();
    services.AddSingleton(jsonRpc);
    services.AddProjXLanguageServerServices();
    AssemblyScanner.GetControllerTypes().ForEach(x => services.AddTransient(x));

    return services.BuildServiceProvider();
  }
}
