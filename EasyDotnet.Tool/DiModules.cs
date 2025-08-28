using System.Diagnostics;
using System.IO.Abstractions;
using DotNetOutdated.Core.Services;
using EasyDotnet.Services;
using EasyDotnet.Utils;
using Microsoft.Extensions.DependencyInjection;
using StreamJsonRpc;

namespace EasyDotnet;

public static class DiModules
{
  //Singleton is scoped per client
  public static ServiceProvider BuildServiceProvider(JsonRpc jsonRpc, SourceLevels levels)
  {
    var services = new ServiceCollection();
    services.AddSingleton(jsonRpc);
    services.AddSingleton<ClientService>();

    services.AddTransient<MsBuildService>();
    services.AddTransient<UserSecretsService>();
    services.AddTransient<NotificationService>();
    services.AddTransient<NugetService>();
    services.AddTransient<OutFileWriterService>();
    services.AddTransient<VsTestService>();
    services.AddTransient<MtpService>();
    services.AddTransient<OutdatedService>();
    services.AddSingleton<IFileSystem, FileSystem>();
    services.AddSingleton<RoslynService>();
    services.AddSingleton<TemplateEngineService>();
    services.AddSingleton<RoslynProjectMetadataCache>();
    services.AddSingleton<IMsBuildHostManager, MsBuildHostManager>();
    services.AddSingleton(new LogService(levels, jsonRpc));

    //Dotnet oudated
    services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();
    services.AddSingleton<IDotNetRunner, DotNetRunner>();
    services.AddSingleton<IDependencyGraphService, DependencyGraphService>();
    services.AddSingleton<IDotNetRestoreService, DotNetRestoreService>();
    services.AddSingleton<INuGetPackageInfoService, NuGetPackageInfoService>();
    services.AddSingleton<INuGetPackageResolutionService, NuGetPackageResolutionService>();

    AssemblyScanner.GetControllerTypes().ForEach(x => services.AddTransient(x));

    return services.BuildServiceProvider();
  }
}