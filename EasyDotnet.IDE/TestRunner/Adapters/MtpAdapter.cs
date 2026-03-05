using System.Runtime.InteropServices;
using EasyDotnet.IDE.MTP.RPC;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.MTP;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// MTP adapter. Spawns a fresh test host process per operation (current behaviour).
/// The process lifecycle is owned entirely here — OperationExecutor knows nothing
/// about processes or TCP.
///
/// Future: keep the Client alive between operations to eliminate spawn latency.
/// </summary>
public sealed class MtpAdapter : ITestAdapter
{
  public async Task DiscoverAsync(string dllPath, Func<DiscoveredTest, Task> onDiscovered, CancellationToken ct)
  {
    var exe = TransformDllPath(dllPath);
    await using var client = await Client.CreateAsync(exe);

    await foreach (var update in client.DiscoverTestsAsync(ct))
    {
      // Skip container/group nodes — we only want executable leaf tests
      if (update.Node.NodeType != "action") continue;
      await onDiscovered(update.ToDiscoveredTest());
    }
  }

  public async Task RunAsync(
      string dllPath,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      CancellationToken ct)
  {
    var exe = TransformDllPath(dllPath);
    await using var client = await Client.CreateAsync(exe);
    var filter = nativeIds
        .Select(id => new RunRequestNode(id, ""))
        .ToArray();

    await foreach (var update in client.RunTestsAsync(filter, ct))
    {
      var result = update.ToTestRunResult();
      if (result is not null) await onResult(result);
    }
  }

  public async Task DebugAsync(
      string dllPath,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      CancellationToken ct)
  {

    var exe = TransformDllPath(dllPath);
    // TODO: wire debug support — MTP debug requires attaching after process spawn
    // For now delegate to run; debug orchestration follows in a subsequent PR
    await RunAsync(exe, nativeIds, onResult, ct);
  }

  private static string TransformDllPath(string dllPath)
  {
    return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
      ? Path.ChangeExtension(dllPath, ".exe")
      : Path.ChangeExtension(dllPath, null);

  }
}