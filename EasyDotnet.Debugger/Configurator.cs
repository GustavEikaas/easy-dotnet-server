using EasyDotnet.Debugger.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDotnet.Debugger;

public static class Configurator
{
  public static IServiceCollection AddDebugger(this IServiceCollection services)
  {
    services.AddSingleton<IDebugSessionFactory, DebugSessionFactory>();
    return services;

  }
}