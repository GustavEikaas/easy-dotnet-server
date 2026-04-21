using System.Collections.Concurrent;
using System.Diagnostics;
using EasyDotnet.AppWrapper.Contracts;
using EasyDotnet.IDE.Interfaces;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.AppWrapper;

public class AppWrapperManager(
    IClientService clientService,
    IEditorProcessManagerService editorProcessManagerService,
    ILogger<AppWrapperManager> logger) : IAppWrapperManager
{
  private readonly ConcurrentDictionary<int, AppWrapperEntry> _wrappers = new();
  private readonly ConcurrentQueue<TaskCompletionSource<AppWrapperEntry>> _pendingSpawns = new();

  public static string GetBackchannelPipeName() => $"easy-dotnet-aw-{Environment.ProcessId}";

  public AppWrapperEntry Register(AppWrapperInitInfo info, JsonRpc rpc)
  {
    var entry = new AppWrapperEntry(info.Pid, rpc);
    _wrappers[info.Pid] = entry;

    rpc.Disconnected += (_, _) => OnDisconnected(info.Pid);

    logger.LogInformation("AppWrapper registered (PID {Pid}). Total: {Count}", info.Pid, _wrappers.Count);

    if (_pendingSpawns.TryDequeue(out var tcs))
    {
      tcs.TrySetResult(entry);
    }

    return entry;
  }

  public async Task<IAppWrapperHandle> GetOrSpawnAsync(CancellationToken ct)
  {
    foreach (var (_, entry) in _wrappers)
    {
      if (entry.TrySetRunning())
      {
        logger.LogInformation("Reusing idle AppWrapper (PID {Pid}).", entry.Pid);
        return new AppWrapperHandle(entry);
      }
    }

    logger.LogInformation("No idle AppWrapper found. Spawning new terminal.");
    return await SpawnAndWaitAsync(ct);
  }

  private async Task<IAppWrapperHandle> SpawnAndWaitAsync(CancellationToken ct)
  {
    var externalTerminal = clientService.ClientOptions?.ExternalTerminal
        ?? throw new InvalidOperationException("No ExternalTerminal configured. Cannot spawn AppWrapper.");

    var appWrapperPath = AppWrapperLocator.GetPath();
    var pipeName = GetBackchannelPipeName();

    var spawnArgs = new List<string>(externalTerminal.Args ?? []);
    spawnArgs.AddRange(["dotnet", "exec", appWrapperPath, "--pipe", pipeName]);

    var startInfo = new ProcessStartInfo
    {
      FileName = externalTerminal.Command,
      UseShellExecute = false,
    };
    foreach (var arg in spawnArgs)
    {
      startInfo.ArgumentList.Add(arg);
    }

    logger.LogInformation("Spawning AppWrapper: {Cmd} {Args}", externalTerminal.Command, string.Join(" ", spawnArgs));
    Process.Start(startInfo);

    var tcs = new TaskCompletionSource<AppWrapperEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
    _pendingSpawns.Enqueue(tcs);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    timeout.Token.Register(() => tcs.TrySetCanceled());

    AppWrapperEntry entry;
    try
    {
      entry = await tcs.Task;
    }
    catch (OperationCanceledException)
    {
      throw new TimeoutException("AppWrapper did not connect within 10 seconds.");
    }

    if (!entry.TrySetRunning())
    {
      throw new InvalidOperationException("Newly registered AppWrapper was already claimed.");
    }

    return new AppWrapperHandle(entry);
  }

  private void OnDisconnected(int pid)
  {
    if (_wrappers.TryRemove(pid, out var entry))
    {
      logger.LogWarning("AppWrapper (PID {Pid}) disconnected and removed.", pid);
      if (entry.CurrentJobId is { } jobId)
      {
        logger.LogWarning("AppWrapper (PID {Pid}) disconnected with active job {JobId}, completing with exit code -1.", pid, jobId);
        editorProcessManagerService.CompleteJob(jobId, -1);
      }
    }
  }
}