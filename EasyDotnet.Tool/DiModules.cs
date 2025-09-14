using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.InteropServices;
using DotNetOutdated.Core.Services;
using EasyDotnet.Services;
using EasyDotnet.Utils;
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
    services.AddSingleton<ClientService>();
    services.AddSingleton<VisualStudioLocator>();
    services.AddSingleton<IFileSystem, FileSystem>();
    services.AddSingleton<RoslynService>();
    services.AddSingleton<SolutionService>();
    services.AddSingleton<ProcessQueueService>();
    services.AddSingleton<TemplateEngineService>();

    services.AddTransient<MsBuildService>();
    services.AddTransient<UserSecretsService>();
    services.AddTransient<NotificationService>();
    services.AddTransient<NugetService>();
    services.AddTransient<VsTestService>();
    services.AddTransient<MtpService>();
    services.AddTransient<OutdatedService>();

    //Dotnet oudated
    services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();
    services.AddSingleton<IDotNetRunner, DotNetRunner>();
    services.AddSingleton<IDependencyGraphService, DependencyGraphService>();
    services.AddSingleton<IDotNetRestoreService, DotNetRestoreService>();
    services.AddSingleton<INuGetPackageInfoService, NuGetPackageInfoService>();
    services.AddSingleton<INuGetPackageResolutionService, NuGetPackageResolutionService>();

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
         _ => Serilog.Events.LogEventLevel.Verbose
       };

  private static void WriteLogHeader()
  {
    var logger = Log.Logger ?? throw new InvalidOperationException("Serilog logger is not initialized.");
    var process = Process.GetCurrentProcess();

    logger.Information("============================================================");
    logger.Information(" [EasyDotnet] Host Server Log");
    logger.Information("============================================================");
    logger.Information($"Timestamp      : {DateTime.UtcNow:O} (UTC)");
    logger.Information($"ProcessId      : {Environment.ProcessId}");
    logger.Information($"Process Name   : {process.ProcessName}");
    logger.Information($"Machine Name   : {Environment.MachineName}");
    logger.Information($"User           : {Environment.UserName}");
    logger.Information($"OS Version     : {Environment.OSVersion}");
    logger.Information($"OS Arch        : {RuntimeInformation.OSArchitecture}");
    logger.Information($"Process Arch   : {RuntimeInformation.ProcessArchitecture}");
    logger.Information($"Framework      : {RuntimeInformation.FrameworkDescription}");
    logger.Information($"CPU Count      : {Environment.ProcessorCount}");
    logger.Information($"Working Set    : {process.WorkingSet64 / 1024 / 1024} MB");
    logger.Information($"Current Dir    : {Environment.CurrentDirectory}");
    logger.Information($"Server Version : {Assembly.GetExecutingAssembly().GetName().Version}");
    logger.Information("============================================================");
    logger.Information("");
  }
}