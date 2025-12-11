using EasyDotnet.ProjXLanguageServer.Services;
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
    services.AddSingleton<IDocumentManager, DocumentManager>();
    services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
    AssemblyScanner.GetControllerTypes().ForEach(x => services.AddTransient(x));

    return services.BuildServiceProvider();
  }
}