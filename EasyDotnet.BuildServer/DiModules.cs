using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using EasyDotnet.BuildServer.Handlers;
using EasyDotnet.BuildServer.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer;

public static class DiModule
{
  public static ServiceProvider BuildServiceProvider(JsonRpc jsonRpc, MsBuildInstance instance, SourceLevels logLevel)
  {
    var services = new ServiceCollection();

    var logLevelState = new LogLevelState(logLevel);
    ConfigureLogging(logLevelState);

    services.AddLogging(builder =>
    {
      builder.ClearProviders();
      builder.AddSerilog(Log.Logger, dispose: true);
    });

    services.AddSingleton(jsonRpc);
    services.AddSingleton(logLevelState);

    services.AddTransient<WatchHandler>();
    services.AddTransient<ProjectPropertiesBatchHandler>();
    services.AddTransient<RestoreHandler>();
    services.AddTransient<BatchBuildHandler>();
    services.AddTransient<SingleFileConvertHandler>();
    services.AddTransient<DiagnosticsHandler>();
    services.AddTransient<PackageReferenceHandler>();
    services.AddTransient<ServerHandler>();

    services.AddSingleton(instance);

    var serviceProvider = services.BuildServiceProvider();

    ConfigureJsonRpc(jsonRpc, serviceProvider, logLevelState);

    return serviceProvider;
  }

  private static void ConfigureLogging(LogLevelState state)
  {
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.ControlledBy(state.Switch)
        .Enrich.WithProperty("ServerType", "BuildServer")
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServerType}] {Message:lj}{NewLine}{Exception}"
        )
        .WriteTo.Sink(state.RingSink)
        .CreateLogger();
    WriteLogHeader();
  }

  private static void ConfigureJsonRpc(JsonRpc jsonRpc, IServiceProvider serviceProvider, LogLevelState logLevelState)
  {
    var logger = serviceProvider.GetRequiredService<ILogger<JsonRpc>>();

    jsonRpc.TraceSource.Switch.Level = logLevelState.Current;
    logLevelState.LevelChanged += l => jsonRpc.TraceSource.Switch.Level = l;
    jsonRpc.TraceSource.Listeners.Clear();
    jsonRpc.TraceSource.Listeners.Add(new JsonRpcTraceListener(logger));

    jsonRpc.Disconnected += (sender, args) => logger.LogWarning("JSON RPC disconnected: {Reason} - {Description}",
              args.Reason, args.Description);

    var handlers = new object[]
    {
      serviceProvider.GetRequiredService<WatchHandler>(),
      serviceProvider.GetRequiredService<ProjectPropertiesBatchHandler>(),
      serviceProvider.GetRequiredService<RestoreHandler>(),
      serviceProvider.GetRequiredService<BatchBuildHandler>(),
      serviceProvider.GetRequiredService<SingleFileConvertHandler>(),
      serviceProvider.GetRequiredService<DiagnosticsHandler>(),
      serviceProvider.GetRequiredService<PackageReferenceHandler>(),
      serviceProvider.GetRequiredService<ServerHandler>(),
    };

    foreach (var handler in handlers)
    {
      jsonRpc.AddLocalRpcTarget(handler, new JsonRpcTargetOptions
      {
        AllowNonPublicInvocation = false
      });
    }
  }

  private static void WriteLogHeader()
  {
    var logger = Log.Logger ?? throw new InvalidOperationException("Serilog logger not initialized");
    var process = Process.GetCurrentProcess();
    var assembly = Assembly.GetExecutingAssembly();

    logger.Information("============================================================");
    logger.Information(" EasyDotnet Build Server");
    logger.Information("============================================================");
    logger.Information("Timestamp      : {Timestamp}", DateTime.UtcNow.ToString("O"));
    logger.Information("Process Name   : {ProcessName}", process.ProcessName);
    logger.Information("Machine Name   : {MachineName}", Environment.MachineName);
    logger.Information("User           : {UserName}", Environment.UserName);
    logger.Information("OS Version     : {OSVersion}", Environment.OSVersion);
    logger.Information("OS Arch        : {OSArch}", RuntimeInformation.OSArchitecture);
    logger.Information("Process Arch   : {ProcessArch}", RuntimeInformation.ProcessArchitecture);
    logger.Information("Framework      : {Framework}", RuntimeInformation.FrameworkDescription);
    logger.Information("CPU Count      : {CPUCount}", Environment.ProcessorCount);
    logger.Information("Working Set    : {WorkingSet} MB", process.WorkingSet64 / 1024 / 1024);
    logger.Information("Current Dir    : {CurrentDir}", Environment.CurrentDirectory);
    logger.Information("Server Version : {Version}", assembly.GetName().Version);
    logger.Information("============================================================");
  }

  private class JsonRpcTraceListener(ILogger<JsonRpc> logger) : TraceListener
  {
    public override void Write(string? message)
    {
      if (!string.IsNullOrEmpty(message))
      {
        logger.LogDebug("{Message}", message);
      }
    }

    public override void WriteLine(string? message)
    {
      if (!string.IsNullOrEmpty(message))
      {
        logger.LogDebug("{Message}", message);
      }
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
      var logLevel = eventType switch
      {
        TraceEventType.Critical => LogLevel.Critical,
        TraceEventType.Error => LogLevel.Error,
        TraceEventType.Warning => LogLevel.Warning,
        TraceEventType.Information => LogLevel.Information,
        _ => LogLevel.Debug
      };

      logger.Log(logLevel, "[{Source}] {Message}", source, message);
    }
  }
}