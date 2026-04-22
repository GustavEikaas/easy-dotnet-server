using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using EasyDotnet.BuildServer.Handlers;
using EasyDotnet.BuildServer.Logging;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer;

public static class DiModule
{
  public sealed record BuildServerServices(Logger Logger, LogLevelState LogLevelState);

  public static BuildServerServices Bootstrap(JsonRpc jsonRpc, MsBuildInstance instance, SourceLevels logLevel)
  {
    var logLevelState = new LogLevelState(logLevel);
    var logger = new Logger(logLevelState);

    WriteLogHeader(logger);

    var watchHandler = new WatchHandler(instance);
    var projectPropertiesBatchHandler = new ProjectPropertiesBatchHandler();
    var restoreHandler = new RestoreHandler();
    var batchBuildHandler = new BatchBuildHandler();
    var singleFileConvertHandler = new SingleFileConvertHandler(restoreHandler, projectPropertiesBatchHandler, logger);
    var diagnosticsHandler = new DiagnosticsHandler(instance);
    var packageReferenceHandler = new PackageReferenceHandler();
    var serverHandler = new ServerHandler(logLevelState, logger);

    ConfigureJsonRpc(jsonRpc, logger, logLevelState, new object[]
    {
      watchHandler,
      projectPropertiesBatchHandler,
      restoreHandler,
      batchBuildHandler,
      singleFileConvertHandler,
      diagnosticsHandler,
      packageReferenceHandler,
      serverHandler,
    });

    return new BuildServerServices(logger, logLevelState);
  }

  private static void ConfigureJsonRpc(JsonRpc jsonRpc, Logger logger, LogLevelState logLevelState, object[] handlers)
  {
    jsonRpc.TraceSource.Switch.Level = logLevelState.Current;
    logLevelState.LevelChanged += l => jsonRpc.TraceSource.Switch.Level = l;
    jsonRpc.TraceSource.Listeners.Clear();
    jsonRpc.TraceSource.Listeners.Add(new JsonRpcTraceListener(logger));

    jsonRpc.Disconnected += (sender, args) => logger.LogWarning("JSON RPC disconnected: {Reason} - {Description}",
              args.Reason, args.Description);

    foreach (var handler in handlers)
    {
      jsonRpc.AddLocalRpcTarget(handler, new JsonRpcTargetOptions
      {
        AllowNonPublicInvocation = false
      });
    }
  }

  private static void WriteLogHeader(Logger logger)
  {
    var process = Process.GetCurrentProcess();
    var assembly = Assembly.GetExecutingAssembly();

    logger.LogInformation("============================================================");
    logger.LogInformation(" EasyDotnet Build Server");
    logger.LogInformation("============================================================");
    logger.LogInformation("Timestamp      : {Timestamp}", DateTime.UtcNow.ToString("O"));
    logger.LogInformation("Process Name   : {ProcessName}", process.ProcessName);
    logger.LogInformation("Machine Name   : {MachineName}", Environment.MachineName);
    logger.LogInformation("User           : {UserName}", Environment.UserName);
    logger.LogInformation("OS Version     : {OSVersion}", Environment.OSVersion);
    logger.LogInformation("OS Arch        : {OSArch}", RuntimeInformation.OSArchitecture);
    logger.LogInformation("Process Arch   : {ProcessArch}", RuntimeInformation.ProcessArchitecture);
    logger.LogInformation("Framework      : {Framework}", RuntimeInformation.FrameworkDescription);
    logger.LogInformation("CPU Count      : {CPUCount}", Environment.ProcessorCount);
    logger.LogInformation("Working Set    : {WorkingSet} MB", process.WorkingSet64 / 1024 / 1024);
    logger.LogInformation("Current Dir    : {CurrentDir}", Environment.CurrentDirectory);
    logger.LogInformation("Server Version : {Version}", assembly.GetName().Version);
    logger.LogInformation("============================================================");
  }

  private class JsonRpcTraceListener(Logger logger) : TraceListener
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
      var level = eventType switch
      {
        TraceEventType.Critical => SourceLevels.Critical,
        TraceEventType.Error => SourceLevels.Error,
        TraceEventType.Warning => SourceLevels.Warning,
        TraceEventType.Information => SourceLevels.Information,
        _ => SourceLevels.Verbose,
      };

      logger.Log(level, "[{Source}] {Message}", source, message);
    }
  }
}