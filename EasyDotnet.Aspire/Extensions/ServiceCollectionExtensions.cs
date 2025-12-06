using EasyDotnet.Aspire.Server;
using EasyDotnet.Aspire.Server.Handlers;
using EasyDotnet.Aspire.Services;
using EasyDotnet.Aspire.Session;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDotnet.Aspire.Extensions;

public static class ServiceCollectionExtensions
{
  /// <summary>
  /// Adds Aspire infrastructure with a custom run session handler
  /// </summary>
  public static IServiceCollection AddAspireInfrastructure<THandler>(
      this IServiceCollection services,
      Action<DcpServerOptions>? configureOptions = null)
      where THandler : class, IRunSessionHandler
  {
    if (configureOptions != null)
    {
      services.Configure(configureOptions);
    }
    else
    {
      services.Configure<DcpServerOptions>(options => { });
    }

    services.AddSingleton<IDcpServer, DcpServer>();
    services.AddSingleton<IAspireSessionManager, AspireSessionManager>();
    services.AddSingleton<AspireCliProcessFactory>();
    services.AddTransient<IAspireService, AspireService>();
    services.AddScoped<IRunSessionHandler, THandler>();

    return services;
  }

  /// <summary>
  /// Ensures the DCP server is started (call this on application startup)
  /// </summary>
  public static async Task<IServiceProvider> EnsureDcpServerStartedAsync(
      this IServiceProvider serviceProvider,
      CancellationToken cancellationToken = default)
  {
    var dcpServer = serviceProvider.GetRequiredService<IDcpServer>();
    await dcpServer.EnsureStartedAsync(cancellationToken);
    return serviceProvider;
  }
}