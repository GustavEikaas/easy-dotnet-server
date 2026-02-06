using EasyDotnet.BuildServer.Handlers;
using Microsoft.Build.Locator;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer;

static class Program
{
  static async Task Main(string[] args)
  {
    // ---------------------------------------------------------
    // PHASE 1: BOOTSTRAP
    // We cannot touch ANY "Microsoft.Build.*" types here yet.
    // ---------------------------------------------------------

    // 1. Debugging Helper (Optional)
    // If you need to attach a debugger from VS, uncomment this:
    // if (args.Contains("--debug")) { Debugger.Launch(); }

    // 2. Find the SDK that matches the CURRENT Runtime.
    // If this process was launched via "dotnet exec", Environment.Version 
    // tells us which runtime is actually active.
    var currentRuntimeVersion = Environment.Version;

    // Query all SDKs installed on the machine
    var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();

    // Filter: Find the SDK that matches our major version (e.g. 8.x or 10.x)
    // We sort by descending to get the newest feature band (e.g. 8.0.400 over 8.0.100)
    var matchingInstance = instances
        .Where(i => i.Version.Major == currentRuntimeVersion.Major)
        .OrderByDescending(i => i.Version)
        .FirstOrDefault();

    if (matchingInstance == null)
    {
      // Log to Stderr (StreamJsonRpc uses Stdout, so we can't pollute it)
      Console.Error.WriteLine($"[Error] Could not find an MSBuild SDK for Runtime {currentRuntimeVersion}");
      Environment.Exit(1);
      return;
    }

    // 3. Register the SDK.
    // This unlocks the ability to load "Microsoft.Build.*" assemblies.
    MSBuildLocator.RegisterInstance(matchingInstance);

    // Log success to Stderr
    Console.Error.WriteLine($"[Info] BuildServer running on {currentRuntimeVersion}");
    Console.Error.WriteLine($"[Info] Registered MSBuild: {matchingInstance.MSBuildPath}");

    // ---------------------------------------------------------
    // PHASE 2: HANDOFF
    // Now call a separate method that uses MSBuild types.
    // ---------------------------------------------------------
    await RunRpcServer();
  }

  // This attribute prevents the JIT from loading this method (and crashing)
  // before MSBuildLocator has finished its job.
  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  static async Task RunRpcServer()
  {
    // Setup JSON-RPC over Standard Input/Output
    var stream = new HeaderDelimitedMessageHandler(
        Console.OpenStandardOutput(),
        Console.OpenStandardInput()
    );

    var rpc = new JsonRpc(stream);
    var ideLogger = rpc.Attach<IIdeLogger>();

    // Register your service class (this is where your logic lives)
    var buildService = new BuildService(ideLogger);

    // 4. Register the Service (So IDE can call us)
    rpc.AddLocalRpcTarget(buildService);

    // Start listening
    rpc.StartListening();

    // Wait until the connection closes (or the IDE kills us)
    await rpc.Completion;
  }
}