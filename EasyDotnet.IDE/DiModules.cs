using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.InteropServices;
using DotNetOutdated.Core.Services;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Debugger;
using EasyDotnet.IDE;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Utils;
using EasyDotnet.Infrastructure.Aspire;
using EasyDotnet.Infrastructure.Editor;
using EasyDotnet.Infrastructure.EntityFramework;
using EasyDotnet.Infrastructure.Process;
using EasyDotnet.Infrastructure.Services;
using EasyDotnet.Infrastructure.Settings;
using EasyDotnet.Services;
using EasyDotnet.TestRunner.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StreamJsonRpc;

namespace EasyDotnet;

public static class DiModules
{
  public static ServiceProvider BuildServiceProvider(JsonRpc jsonRpc, SourceLevels levels)
  {
    var services = new ServiceCollection();

    ConfigureLogging(levels);

    services.AddLogging(builder =>
        {
          builder.ClearProviders();
          builder.AddSerilog(Log.Logger, dispose: true);
        });

    services.AddMemoryCache();
    services.AddSingleton(jsonRpc);
    services.AddSingleton<IClientService, ClientService>();
    services.AddSingleton<IVisualStudioLocator, VisualStudioLocator>();
    services.AddSingleton<IFileSystem, FileSystem>();
    services.AddSingleton<RoslynService>();
    services.AddSingleton<ISolutionService, SolutionService>();
    services.AddSingleton<IProcessQueue, ProcessQueue>();
    services.AddSingleton<TemplateEngineService>();
    services.AddSingleton<IDebugSessionManager, DebugSessionManager>();
    services.AddSingleton<IDebugOrchestrator, DebugOrchestrator>();
    services.AddSingleton<IAppPathsService, AppPathsService>();
    services.AddSingleton<UpdateCheckerService>();
    services.AddSingleton<SettingsFileResolver>();
    services.AddSingleton<SettingsSerializer>();
    services.AddSingleton<SettingsService>();
    services.AddSingleton<SettingsGarbageCollector>();
    services.AddSingleton<IEditorProcessManagerService, EditorProcessManagerService>();
    services.AddSingleton<IEditorService, EditorService>();

    services.AddTransient<IProgressScopeFactory, ProgressScopeFactory>();
    services.AddTransient<IMsBuildService, MsBuildService>();
    services.AddTransient<IAspireService, AspireService>();
    services.AddTransient<IJsonCodeGenService, JsonCodeGenService>();
    services.AddTransient<UserSecretsService>();
    services.AddTransient<EntityFrameworkService>();
    services.AddTransient<ILaunchProfileService, LaunchProfileService>();
    services.AddTransient<INotificationService, NotificationService>();
    services.AddTransient<NugetService>();
    services.AddTransient<IVsTestService, VsTestService>();
    services.AddTransient<MtpService>();
    services.AddTransient<OutdatedService>();

    //Dotnet oudated
    services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();
    services.AddSingleton<IDotNetRunner, DotNetRunner>();
    services.AddSingleton<IDependencyGraphService, DependencyGraphService>();
    services.AddSingleton<IDotNetRestoreService, DotNetRestoreService>();
    services.AddSingleton<INuGetPackageInfoService, NuGetPackageInfoService>();
    services.AddSingleton<INuGetPackageResolutionService, NuGetPackageResolutionService>();

    services.AddTestRunner();
    services.AddDebugger();

    AssemblyScanner.GetControllerTypes().ForEach(x => services.AddTransient(x));

    var serviceProvider = services.BuildServiceProvider();

    var logger = serviceProvider.GetRequiredService<ILogger<JsonRpc>>();
    jsonRpc.TraceSource.Switch.Level = levels;
    jsonRpc.TraceSource.Listeners.Clear();
    jsonRpc.TraceSource.Listeners.Add(new JsonRpcLogger(logger));

    return serviceProvider;
  }

  private static void ConfigureLogging(SourceLevels levels)
  {
    string? logFile = null;
    if (levels.HasFlag(SourceLevels.Verbose))
    {
      var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
      Directory.CreateDirectory(logDir);
      logFile = Path.Combine(logDir,
          $"easy-dotnet-{DateTime.UtcNow:yyyyMMdd_HHmmss}-{Environment.ProcessId}.log");
    }

    var serilogConfig = new LoggerConfiguration()
        .MinimumLevel.Is(ConvertLogLevel(levels))
        .WriteTo.Console();

    if (!string.IsNullOrEmpty(logFile))
    {
      serilogConfig = serilogConfig.WriteTo.File(logFile, rollingInterval: RollingInterval.Day);
    }

    Log.Logger = serilogConfig.CreateLogger();
    WriteLogHeader();
  }

  private static Serilog.Events.LogEventLevel ConvertLogLevel(SourceLevels level) =>
       level switch
       {
         SourceLevels.Critical => Serilog.Events.LogEventLevel.Fatal,
         SourceLevels.Error => Serilog.Events.LogEventLevel.Error,
         SourceLevels.Warning => Serilog.Events.LogEventLevel.Warning,
         SourceLevels.Information => Serilog.Events.LogEventLevel.Information,
         SourceLevels.Verbose => Serilog.Events.LogEventLevel.Verbose,
         _ => Serilog.Events.LogEventLevel.Information
       };

  private static void WriteLogHeader()
  {
    var logger = Log.Logger ?? throw new InvalidOperationException("Serilog logger is not initialized.");
    var process = Process.GetCurrentProcess();

    logger.Debug("============================================================");
    logger.Debug(" [EasyDotnet] Host Server Log");
    logger.Debug("============================================================");
    logger.Debug($"Timestamp      : {DateTime.UtcNow:O} (UTC)");
    logger.Debug($"ProcessId      : {Environment.ProcessId}");
    logger.Debug($"Process Name   : {process.ProcessName}");
    logger.Debug($"Machine Name   : {Environment.MachineName}");
    logger.Debug($"User           : {Environment.UserName}");
    logger.Debug($"OS Version     : {Environment.OSVersion}");
    logger.Debug($"OS Arch        : {RuntimeInformation.OSArchitecture}");
    logger.Debug($"Process Arch   : {RuntimeInformation.ProcessArchitecture}");
    logger.Debug($"Framework      : {RuntimeInformation.FrameworkDescription}");
    logger.Debug($"CPU Count      : {Environment.ProcessorCount}");
    logger.Debug($"Working Set    : {process.WorkingSet64 / 1024 / 1024} MB");
    logger.Debug($"Current Dir    : {Environment.CurrentDirectory}");
    logger.Debug($"Server Version : {Assembly.GetExecutingAssembly().GetName().Version}");
    logger.Debug("============================================================");
    logger.Debug("");
  }
}