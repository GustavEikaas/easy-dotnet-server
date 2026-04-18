using Microsoft.Extensions.DependencyInjection;

namespace EasyDotnet.IDE.PackageManager;

public static class PackageManagerDiModule
{
  public static IServiceCollection AddPackageManager(this IServiceCollection services)
  {
    services.AddTransient<PackageManagerService>();
    return services;
  }
}