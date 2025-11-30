using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDotnet.Debugger;

public static class Configurator
{
  public static IServiceCollection AddDebugger(this IServiceCollection services)
  {
    services.AddTransient<ValueConverterService>();
    services.AddSingleton<INetcoreDbgService, NetcoreDbgService>();
    return services;

  }
}