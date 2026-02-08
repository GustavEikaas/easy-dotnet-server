using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer;

static class Program
{
  static async Task<int> Main(string[] args)
  {
    var logLevel = ParseLogLevel(args);
    var pipe = ParsePipe(args);

    if (pipe is null)
    {
      Console.Error.WriteLine("No pipe passed");
      return 1;
    }

    if (!RegisterMSBuild())
    {
      return 1;
    }

    return await RunClient(logLevel, pipe);
  }

  private static bool RegisterMSBuild()
  {
    try
    {
      var currentRuntimeVersion = Environment.Version;
      var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();

      Console.Error.WriteLine($"[Info] Found {instances.Count} MSBuild instance(s)");

      var matchingInstance = instances
          .Where(i => i.Version.Major == currentRuntimeVersion.Major)
          .OrderByDescending(i => i.Version)
          .FirstOrDefault();

      if (matchingInstance == null)
      {
        Console.Error.WriteLine($"[Error] Could not find an MSBuild SDK for Runtime {currentRuntimeVersion}");
        Console.Error.WriteLine($"[Error] Available instances:");
        foreach (var instance in instances)
        {
          Console.Error.WriteLine($"  - {instance.Name} (v{instance.Version}) at {instance.MSBuildPath}");
        }
        return false;
      }

      MSBuildLocator.RegisterInstance(matchingInstance);

      Console.Error.WriteLine($"[Info] BuildServer running on .NET {currentRuntimeVersion}");
      Console.Error.WriteLine($"[Info] Registered MSBuild: {matchingInstance.Name} (v{matchingInstance.Version})");
      Console.Error.WriteLine($"[Info] MSBuild Path: {matchingInstance.MSBuildPath}");

      return true;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"[Error] Failed to register MSBuild: {ex.Message}");
      Console.Error.WriteLine($"[Error] Stack Trace: {ex.StackTrace}");
      return false;
    }
  }

  private static async Task<HeaderDelimitedMessageHandler> CreateClientMessageHandlerAsync(
    string pipeName)
  {
    var pipe = new NamedPipeClientStream(
      serverName: ".",
      pipeName: pipeName,
      direction: PipeDirection.InOut,
      options: PipeOptions.Asynchronous
    );

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    try
    {
      await pipe.ConnectAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
      throw new TimeoutException($"Timed out after 5 seconds waiting for pipe '{pipeName}'.");
    }

    return new HeaderDelimitedMessageHandler(pipe, pipe);
  }

  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  private static async Task<int> RunClient(
  SourceLevels logLevel,
  string pipeName)
  {
    var messageHandler = await CreateClientMessageHandlerAsync(pipeName);

    var jsonRpc = new JsonRpc(messageHandler);

    var serviceProvider = DiModule.BuildServiceProvider(jsonRpc, logLevel);
    var logger = serviceProvider.GetRequiredService<ILogger<JsonRpc>>();

    logger.LogInformation("JSON-RPC client initialized");
    logger.LogInformation("Connected via named pipe {PipeName}", pipeName);

    jsonRpc.StartListening();

    await jsonRpc.Completion;

    return 0;
  }


  private static string? ParsePipe(string[] args)
  {
    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i];

      if (arg.Equals("--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
      {
        return args[i + 1];
      }
    }

    return null;
  }

  private static SourceLevels ParseLogLevel(string[] args)
  {
    foreach (var arg in args)
    {
      if (arg.StartsWith("--log-level=", StringComparison.OrdinalIgnoreCase))
      {
        var level = arg["--log-level=".Length..];
        return level.ToLowerInvariant() switch
        {
          "verbose" => SourceLevels.Verbose,
          "information" => SourceLevels.Information,
          "warning" => SourceLevels.Warning,
          "error" => SourceLevels.Error,
          "critical" => SourceLevels.Critical,
          _ => SourceLevels.Information
        };
      }
    }

#if DEBUG
    return SourceLevels.Verbose;
#else
    return SourceLevels.Information;
#endif
  }
}