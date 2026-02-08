using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer;

static class Program
{
  static async Task<int> Main(string[] args)
  {
    var logLevel = ParseLogLevel(args);

    if (!RegisterMSBuild())
    {
      return 1;
    }

    return await RunServerAsync(logLevel);
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

  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  private static async Task<int> RunServerAsync(SourceLevels logLevel)
  {
    var messageHandler = new HeaderDelimitedMessageHandler(
        Console.OpenStandardOutput(),
        Console.OpenStandardInput()
    );

    var jsonRpc = new JsonRpc(messageHandler);

    var serviceProvider = DiModule.BuildServiceProvider(jsonRpc, logLevel);
    var logger = serviceProvider.GetRequiredService<ILogger<JsonRpc>>();

    logger.LogInformation("Build server initialized successfully");
    logger.LogInformation("Listening for JSON RPC requests on stdin/stdout");

    jsonRpc.StartListening();

    logger.LogInformation("Build server is now listening");

    await jsonRpc.Completion;

    return 0;
  }

  private static SourceLevels ParseLogLevel(string[] args)
  {
    foreach (var arg in args)
    {
      if (arg.StartsWith("--log-level=", StringComparison.OrdinalIgnoreCase))
      {
        var level = arg.Substring("--log-level=".Length);
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