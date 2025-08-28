using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using EasyDotnet.Services;
using EasyDotnet.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet;

public static class JsonRpcServerBuilder
{
  public static JsonRpc Build(Stream writer, Stream reader, Func<JsonRpc, SourceLevels, ServiceProvider>? buildServiceProvider = null, SourceLevels? logLevel = SourceLevels.Off)
  {
    var formatter = CreateJsonMessageFormatter();
    var handler = new HeaderDelimitedMessageHandler(writer, reader, formatter);
    var jsonRpc = new JsonRpc(handler);

    var sp = buildServiceProvider is not null ? buildServiceProvider(jsonRpc, logLevel ?? SourceLevels.Off) : DiModules.BuildServiceProvider(jsonRpc, logLevel ?? SourceLevels.Off);
    RegisterControllers(jsonRpc, sp);
    EnableTracingIfNeeded(jsonRpc, logLevel ?? SourceLevels.Off);

    jsonRpc.Completion.ContinueWith(x => sp.GetRequiredService<IMsBuildHostManager>().StopAll());

    return jsonRpc;
  }

  private static JsonMessageFormatter CreateJsonMessageFormatter() => new()
  {
    JsonSerializer = { ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }}
  };

  private static void RegisterControllers(JsonRpc jsonRpc, IServiceProvider provider) => AssemblyScanner.GetControllerTypes().ForEach(x => jsonRpc.AddLocalRpcTarget(provider.GetRequiredService(x)));

  private static void EnableTracingIfNeeded(JsonRpc jsonRpc, SourceLevels logLevel)
  {
    var ts = jsonRpc.TraceSource;

#if DEBUG
    ts.Switch.Level = SourceLevels.Verbose;
    ts.Listeners.Add(new ConsoleTraceListener());
#endif


    if (logLevel != SourceLevels.Off)
    {
      ts.Switch.Level = logLevel;
      var logDir = Path.Combine(Path.GetTempPath(), "easy-dotnet-server");
      Directory.CreateDirectory(logDir);

      var logFile = Path.Combine(
          logDir,
          $"jsonrpc-easy-dotnet-server-{DateTime.UtcNow:yyyyMMdd_HHmmss}-{Environment.ProcessId}.log");
      var traceSource = new TraceSource("StreamJsonRpc", logLevel);
      var listener = new TextWriterTraceListener(logFile);
      if (logLevel == SourceLevels.Verbose)
      {
        WriteLogHeader(listener);
      }
      jsonRpc.TraceSource.Listeners.Add(listener);
      Trace.AutoFlush = true;
    }
  }

  private static void WriteLogHeader(TextWriterTraceListener listener)
  {
    var process = Process.GetCurrentProcess();

    listener.WriteLine("============================================================");
    listener.WriteLine(" [EasyDotnet] Host Server Log");
    listener.WriteLine("============================================================");
    listener.WriteLine($"Timestamp      : {DateTime.UtcNow:O} (UTC)");
    listener.WriteLine($"ProcessId      : {Environment.ProcessId}");
    listener.WriteLine($"Process Name   : {process.ProcessName}");
    listener.WriteLine($"Machine Name   : {Environment.MachineName}");
    listener.WriteLine($"User           : {Environment.UserName}");
    listener.WriteLine($"OS Version     : {Environment.OSVersion}");
    listener.WriteLine($"OS Arch        : {RuntimeInformation.OSArchitecture}");
    listener.WriteLine($"Process Arch   : {RuntimeInformation.ProcessArchitecture}");
    listener.WriteLine($"Framework      : {RuntimeInformation.FrameworkDescription}");
    listener.WriteLine($"CPU Count      : {Environment.ProcessorCount}");
    listener.WriteLine($"Working Set    : {process.WorkingSet64 / 1024 / 1024} MB");
    listener.WriteLine($"Current Dir    : {Environment.CurrentDirectory}");
    listener.WriteLine($"Server Version : {Assembly.GetExecutingAssembly().GetName().Version}");
    listener.WriteLine("============================================================");
    listener.WriteLine("");
    listener.Flush();
  }
}