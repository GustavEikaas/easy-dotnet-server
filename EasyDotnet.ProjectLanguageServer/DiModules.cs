using EasyDotnet.ProjectLanguageServer.Services;
using EasyDotnet.ProjectLanguageServer.Utils;
using Microsoft.Extensions.DependencyInjection;
using StreamJsonRpc;

namespace EasyDotnet.ProjectLanguageServer;

public static class DiModules
{
  public static ServiceProvider BuildServiceProvider(JsonRpc jsonRpc)
  {
    var services = new ServiceCollection();
    services.AddSingleton(jsonRpc);
    services.AddSingleton<IDocumentManager, DocumentManager>();
    AssemblyScanner.GetControllerTypes().ForEach(x => services.AddTransient(x));

    return services.BuildServiceProvider();
  }
}
