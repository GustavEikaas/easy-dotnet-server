using System.Runtime.InteropServices;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.MTP.RPC;
using EasyDotnet.IDE.Services;
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
public sealed class MtpAdapter(
  IEditorService editorService,
  IDebugStrategyFactory debugStrategyFactory,
  IDebugOrchestrator debugOrchestrator
) : ITestAdapter
{
  public async Task DiscoverAsync(ValidatedDotnetProject project, Func<DiscoveredTest, Task> onDiscovered, CancellationToken ct)
  {
    var exe = TransformDllPath(project.TargetPath);
    await using var client = await Client.CreateAsync(exe);

    await foreach (var update in client.DiscoverTestsAsync(ct))
    {
      if (update.Node.NodeType != "action") continue;
      await onDiscovered(update.ToDiscoveredTest());
    }
  }

  public async Task RunAsync(
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      CancellationToken ct)
  {
    var exe = TransformDllPath(project.TargetPath);
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
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      CancellationToken ct)
  {

    var exe = TransformDllPath(project.TargetPath);
    await using var client = await Client.CreateAsync(exe);
    var session = await debugOrchestrator.StartClientDebugSessionAsync(
      project.ProjectFullPath,
      new(project.ProjectFullPath, project.TargetFramework, null, null),
      debugStrategyFactory.CreateStandardAttachStrategy(client.DebugeeProcessId),
      ct);

    await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
    await session.ProcessStarted;
    await Task.Delay(1000, ct);
    var filter = nativeIds
        .Select(id => new RunRequestNode(id, ""))
        .ToArray();

    await foreach (var update in client.RunTestsAsync(filter, ct))
    {
      var result = update.ToTestRunResult();
      if (result is not null) await onResult(result);
    }
  }

  private static string TransformDllPath(string dllPath)
  {
    return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
      ? Path.ChangeExtension(dllPath, ".exe")
      : Path.ChangeExtension(dllPath, null);

  }
}