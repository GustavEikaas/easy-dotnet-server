using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using EasyDotnet.MsBuild.Contracts;
using EasyDotnet.MsBuildSdk.Controllers;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.MsBuildSdk;


class Program
{
  static (VisualStudioInstance? instance, SdkInstallation[] installations) BootstrapMsBuild()
  {
    MSBuildLocator.AllowQueryAllRuntimeVersions = true;
    var instances = MSBuildLocator.QueryVisualStudioInstances().Where(x => x.DiscoveryType == DiscoveryType.DotNetSdk).ToList();
    var monikers = instances.Select(x => new SdkInstallation(x.Name, $"net{x.Version.Major}.0", x.Version, x.MSBuildPath, x.VisualStudioRootPath)).ToArray();
    var r = MSBuildLocator.RegisterDefaults();
    return (r, monikers);
  }

  static async Task Main(string[] args)
  {

    SourceLevels? logLevel = null;

    var logArg = args
        .SkipWhile(a => !a.Equals("--logLevel", StringComparison.OrdinalIgnoreCase))
        .Skip(1)
        .LastOrDefault();

    if (logArg != null && Enum.TryParse<SourceLevels>(logArg, true, out var parsedLevel))
    {
      logLevel = parsedLevel;
    }

    var (instance, monikers) = BootstrapMsBuild();
    var pipe = args[0];
    await StartServerAsync(pipe, instance, monikers, logLevel ?? SourceLevels.Off);
  }

  private static async Task StartServerAsync(string pipeName, VisualStudioInstance? instance, SdkInstallation[] monikers, SourceLevels sourceLevel)
  {
    var clientId = 0;
    while (true)
    {
      var stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
      Console.WriteLine($"Named pipe server started: {pipeName}");
      await stream.WaitForConnectionAsync();
      _ = RespondToRpcRequestsAsync(stream, ++clientId, instance, monikers, sourceLevel);
    }
  }

  private static async Task RespondToRpcRequestsAsync(Stream stream, int clientId, VisualStudioInstance? instance, SdkInstallation[] monikers, SourceLevels sourceLevel)
  {
    var jsonMessageFormatter = new JsonMessageFormatter();
    jsonMessageFormatter.JsonSerializer.ContractResolver = new DefaultContractResolver
    {
      NamingStrategy = new CamelCaseNamingStrategy(),
    };

    var handler = new HeaderDelimitedMessageHandler(stream, stream, jsonMessageFormatter);
    var jsonRpc = new JsonRpc(handler);
    jsonRpc.AddLocalRpcTarget(new MsbuildController(monikers));

    if (sourceLevel != SourceLevels.Off)
    {
      var ts = jsonRpc.TraceSource;

      ts.Switch.Level = sourceLevel;
      var logDir = Path.Combine(Path.GetTempPath(), "easy-dotnet-server");
      Directory.CreateDirectory(logDir);

      var logFile = Path.Combine(
          logDir,
          $"jsonrpc-msbuild-sdk-server-{DateTime.UtcNow:yyyyMMdd_HHmmss}-{Environment.ProcessId}.log");
      var listener = new TextWriterTraceListener(logFile);
      if (sourceLevel == SourceLevels.Verbose)
      {
        WriteLogHeader(listener, instance);
      }
      jsonRpc.TraceSource.Listeners.Add(listener);
      Trace.AutoFlush = true;
    }

    jsonRpc.StartListening();
    Console.WriteLine($"JSON-RPC listener attached to #{clientId}. Waiting for requests...");
    await jsonRpc.Completion;
    await Console.Error.WriteLineAsync($"Connection #{clientId} terminated.");
  }

  private static void WriteLogHeader(TextWriterTraceListener listener, VisualStudioInstance? instance)
  {
    var process = Process.GetCurrentProcess();


    var asm = typeof(ProjectCollection).Assembly;
    var asmName = asm.GetName();

    Console.WriteLine($"Assembly: {asmName.Name}");
    Console.WriteLine($"Version : {asmName.Version}");
    Console.WriteLine($"Location: {asm.Location}");

    listener.WriteLine("============================================================");
    listener.WriteLine(" [EasyDotnet] MsBuildSdk Server Log");
    listener.WriteLine("============================================================");
    listener.WriteLine($"Timestamp              : {DateTime.UtcNow:O} (UTC)");
    listener.WriteLine($"ProcessId              : {Environment.ProcessId}");
    listener.WriteLine($"Process Name           : {process.ProcessName}");
    listener.WriteLine($"Machine Name           : {Environment.MachineName}");
    listener.WriteLine($"User                   : {Environment.UserName}");
    listener.WriteLine($"OS Version             : {Environment.OSVersion}");
    listener.WriteLine($"OS Arch                : {RuntimeInformation.OSArchitecture}");
    listener.WriteLine($"Process Arch           : {RuntimeInformation.ProcessArchitecture}");
    listener.WriteLine($"Framework              : {RuntimeInformation.FrameworkDescription}");
    listener.WriteLine($"CPU Count              : {Environment.ProcessorCount}");
    listener.WriteLine($"Working Set            : {process.WorkingSet64 / 1024 / 1024} MB");
    listener.WriteLine($"Current Dir            : {Environment.CurrentDirectory}");
    if (instance is not null)
    {
      listener.WriteLine($"SDK Name               : {instance.Name}");
      listener.WriteLine($"SDK Version            : {instance.Version}");
      listener.WriteLine($"SDK Path               : {instance.MSBuildPath}");
      listener.WriteLine($"MSBuild Version        : {asmName.Version}");
      listener.WriteLine($"MSBuild Assembly Path  : {asm.Location}");
    }
    listener.WriteLine("============================================================");
    listener.WriteLine("");
    listener.Flush();
  }
}