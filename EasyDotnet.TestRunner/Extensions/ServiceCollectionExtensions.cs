using EasyDotnet.TestRunner.Abstractions;
using EasyDotnet.TestRunner.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDotnet.TestRunner.Extensions;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddTestRunner(this IServiceCollection services)
  {
    services.AddSingleton<ITestRunner, TestRunnerService>();
    services.AddSingleton<ITestSessionRegistry, TestSessionRegistry>();
    services.AddSingleton<ITestHierarchyService, TestHierarchyService>();
    return services;
  }
}