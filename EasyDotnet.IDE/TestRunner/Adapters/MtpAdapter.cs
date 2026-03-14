using System.Runtime.InteropServices;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.TestRunner.Adapters.MTP;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC;
using EasyDotnet.IDE.TestRunner.Lock;
using EasyDotnet.IDE.TestRunner.Models;
using Microsoft.Extensions.Logging;

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
  IDebugOrchestrator debugOrchestrator,
  ILogger<MtpAdapter> logger,
  MtpClientFactory clientFactory
) : ITestAdapter
{
  public async Task DiscoverAsync(
      ValidatedDotnetProject project,
      Func<DiscoveredTest, Task> onDiscovered,
      OperationControl control,
      CancellationToken ct)
  {
    var exe = TransformDllPath(project.TargetPath);
    await using var client = await clientFactory.CreateAsync(exe, ct);
    control.RegisterKill(() => client.KillAsync());

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
      OperationControl control,
      CancellationToken ct)
  {
    var exe = TransformDllPath(project.TargetPath);
    await using var client = await clientFactory.CreateAsync(exe, ct);
    control.RegisterKill(() => client.KillAsync());

    var filter = nativeIds
        .Select(id => new RunRequestNode(id, ""))
        .ToArray();

    await foreach (var update in client.RunTestsAsync(filter, ct))
    {
      TestRunResult? result;
      try { result = update.ToTestRunResult(); }
      catch (ArgumentOutOfRangeException ex)
      {
        logger.LogError(ex, "Skipping result with unmapped MTP execution state for {Uid}", update.Node.Uid);
        continue;
      }
      if (result is not null) await onResult(result);
    }
  }

  public async Task DebugAsync(
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      OperationControl control,
      CancellationToken ct)
  {

    var exe = TransformDllPath(project.TargetPath);

    await using var client = await clientFactory.CreateAsync(exe, ct);
    control.RegisterKill(() => client.KillAsync());
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
      TestRunResult? result;
      try { result = update.ToTestRunResult(); }
      catch (ArgumentOutOfRangeException ex)
      {
        logger.LogError(ex, "Skipping result with unmapped MTP execution state for {Uid}", update.Node.Uid);
        continue;
      }
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