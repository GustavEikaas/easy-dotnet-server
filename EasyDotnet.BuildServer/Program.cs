using System.Diagnostics;
using System.IO.Pipes;
using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer;

static class Program
{
  static async Task<int> Main(string[] args)
  {
    var logLevel = ParseLogLevel(args);
    var pipe = ParsePipe(args);
    var logDirectory = ParseLogDirectory(args);

    if (pipe is null || logDirectory is null)
    {
      Console.Error.WriteLine("No --pipe passed or --logDirectory not passed");
      return 1;
    }

    var instance = RegisterMSBuild();

    return await RunServer(logLevel, logDirectory, pipe, instance);
  }

  private static MsBuildInstance RegisterMSBuild()
  {
#pragma warning disable IDE0022 // Use expression body for method
#if NET472
    return RegisterMSBuildFramework();
#else
    return RegisterMSBuildCore();
#pragma warning restore IDE0022 // Use expression body for method
#endif
  }

  private static MsBuildInstance RegisterMSBuildFramework()
  {
    try
    {
      var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();

      var bestInstance = instances
          .OrderByDescending(i => i.Version)
          .FirstOrDefault();

      if (bestInstance == null)
      {
        throw new Exception("[Error] No Visual Studio instances found.");
      }

      MSBuildLocator.RegisterInstance(bestInstance);

      Console.Error.WriteLine($"[Info] BuildServer running on .NET Framework {Environment.Version}");
      Console.Error.WriteLine($"[Info] Registered MSBuild: {bestInstance.Name} (v{bestInstance.Version})");
      Console.Error.WriteLine($"[Info] MSBuild Path: {bestInstance.MSBuildPath}");
      return new MsBuildInstance(bestInstance.Name, $"net{bestInstance.Version.Major}.0", bestInstance.Version, bestInstance.MSBuildPath, bestInstance.VisualStudioRootPath, MsBuildInstanceOrigin.VisualStudio);
    }
    catch (Exception ex)
    {
      throw new Exception($"[Error] Failed to register MSBuild (Framework): {ex.Message}");
    }
  }

  private static MsBuildInstance RegisterMSBuildCore()
  {
    try
    {
      var currentRuntimeVersion = Environment.Version;

      var instances = MSBuildLocator.QueryVisualStudioInstances()
          .Where(i => i.Version.Major == currentRuntimeVersion.Major)
          .ToList();

      Console.Error.WriteLine($"[Info] Found {instances.Count} compatible MSBuild instance(s)");

      var matchingInstance = instances
          .OrderByDescending(i => i.Version)
          .FirstOrDefault();

      if (matchingInstance == null)
      {
        Console.Error.WriteLine($"[Error] Could not find an MSBuild SDK for Runtime {currentRuntimeVersion}");
        Console.Error.WriteLine($"[Error] Ensure you have the .NET SDK installed for this major version.");
        throw new Exception();
      }

      MSBuildLocator.RegisterInstance(matchingInstance);

      Console.Error.WriteLine($"[Info] BuildServer running on .NET Core {currentRuntimeVersion}");
      Console.Error.WriteLine($"[Info] Registered MSBuild: {matchingInstance.Name} (v{matchingInstance.Version})");
      Console.Error.WriteLine($"[Info] MSBuild Path: {matchingInstance.MSBuildPath}");

      return new MsBuildInstance(matchingInstance.Name, $"net{matchingInstance.Version.Major}.0", matchingInstance.Version, matchingInstance.MSBuildPath, matchingInstance.VisualStudioRootPath, MsBuildInstanceOrigin.SDK);
    }
    catch (Exception ex)
    {
      throw new Exception($"[Error] Failed to register MSBuild (Core): {ex.Message}");
    }
  }

  private static async Task<HeaderDelimitedMessageHandler> CreateServerMessageHandlerAsync(
      string pipeName)
  {
    var pipeServer = new NamedPipeServerStream(
      pipeName,
      PipeDirection.InOut,
      maxNumberOfServerInstances: 1,
      transmissionMode: PipeTransmissionMode.Byte,
      options: PipeOptions.Asynchronous
    );

    Console.Error.WriteLine($"[Info] Waiting for connection on pipe: {pipeName}...");

    await pipeServer.WaitForConnectionAsync();

    Console.Error.WriteLine("[Info] Client connected.");

    return new HeaderDelimitedMessageHandler(pipeServer, pipeServer, CreateJsonMessageFormatter());
  }

  private static JsonMessageFormatter CreateJsonMessageFormatter() => new()
  {
    JsonSerializer = { ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }}
  };

  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  private static async Task<int> RunServer(
    SourceLevels logLevel,
    string logDirectory,
    string pipeName,
    MsBuildInstance instance)
  {
    var messageHandler = await CreateServerMessageHandlerAsync(pipeName);

    var jsonRpc = new JsonRpc(messageHandler);

    var serviceProvider = DiModule.BuildServiceProvider(jsonRpc, instance, logLevel, logDirectory);

    var logger = serviceProvider.GetRequiredService<ILogger<JsonRpc>>();

    logger.LogInformation("JSON-RPC Server initialized");
    logger.LogInformation("Listening on named pipe {PipeName}", pipeName);

    jsonRpc.StartListening();

    await jsonRpc.Completion;

    logger.LogInformation("Client disconnected. Shutting down.");

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

  private static string? ParseLogDirectory(string[] args)
  {
    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i];

      if (arg.Equals("--logDirectory", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
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
          "all" => SourceLevels.All,
          "off" => SourceLevels.Off,
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