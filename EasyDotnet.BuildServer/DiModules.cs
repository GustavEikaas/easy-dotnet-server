using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.Handlers;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer;

public static class DiModule
{
  public static ServiceProvider BuildServiceProvider(JsonRpc jsonRpc, VisualStudioInstance instance, SourceLevels logLevel)
  {
    var services = new ServiceCollection();

    ConfigureLogging(logLevel);

    services.AddLogging(builder =>
    {
      builder.ClearProviders();
      builder.AddSerilog(Log.Logger, dispose: true);
    });

    services.AddSingleton(jsonRpc);

    services.AddTransient<SolutionHandler>();
    services.AddTransient<WatchHandler>();

    services.AddSingleton(new SdkInstallation(instance.Name, $"net{instance.Version.Major}.0", instance.Version, instance.MSBuildPath, instance.VisualStudioRootPath));

    var serviceProvider = services.BuildServiceProvider();

    ConfigureJsonRpc(jsonRpc, serviceProvider, logLevel);

    return serviceProvider;
  }

  private static void ConfigureLogging(SourceLevels logLevel)
  {
    string? logFile = null;

    if (logLevel.HasFlag(SourceLevels.Verbose) || logLevel.HasFlag(SourceLevels.Information))
    {
      var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "buildserver");
      Directory.CreateDirectory(logDir);
      logFile = Path.Combine(logDir,
          $"buildserver-{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
    }

    var serilogConfig = new LoggerConfiguration()
        .MinimumLevel.Is(ConvertLogLevel(logLevel))
        .Enrich.WithProperty("ServerType", "BuildServer")
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServerType}] {Message:lj}{NewLine}{Exception}"
        );

    if (!string.IsNullOrEmpty(logFile))
    {
      serilogConfig = serilogConfig.WriteTo.File(
          logFile!,
          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ProcessId}] {Message:lj}{NewLine}{Exception}",
          rollingInterval: RollingInterval.Day,
          retainedFileCountLimit: 7);
    }

    Log.Logger = serilogConfig.CreateLogger();
    WriteLogHeader();
  }

  private static void ConfigureJsonRpc(JsonRpc jsonRpc, IServiceProvider serviceProvider, SourceLevels logLevel)
  {
    var logger = serviceProvider.GetRequiredService<ILogger<JsonRpc>>();

    jsonRpc.TraceSource.Switch.Level = logLevel;
    jsonRpc.TraceSource.Listeners.Clear();
    jsonRpc.TraceSource.Listeners.Add(new JsonRpcTraceListener(logger));

    jsonRpc.Disconnected += (sender, args) => logger.LogWarning("JSON RPC disconnected: {Reason} - {Description}",
              args.Reason, args.Description);

    var handlers = new object[]
    {
      serviceProvider.GetRequiredService<SolutionHandler>(),
      serviceProvider.GetRequiredService<WatchHandler>(),
    };

    foreach (var handler in handlers)
    {
      jsonRpc.AddLocalRpcTarget(handler, new JsonRpcTargetOptions
      {
        AllowNonPublicInvocation = false
      });
    }
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