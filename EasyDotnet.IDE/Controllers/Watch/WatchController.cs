using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Watch;

public class WatchController(
    IEditorService editorService,
    IMsBuildService msBuildService,
    BuildHostManager buildHostManager,
    IDebugStrategyFactory debugStrategyFactory,
    IDebugOrchestrator debugOrchestrator) : BaseController
{
  [JsonRpcMethod("watch/debug")]
  public async Task Watch(string projectPath, CancellationToken cancellationToken)
  {
    var sessionId = Guid.NewGuid().ToString();
    var watchFilesres = await buildHostManager.GetProjectWatchListAsync(new(projectPath, "debug"), cancellationToken);
    var watchFiles = watchFilesres.Projects.SelectMany(x => x.Value.Files).ToArray();
    if (watchFiles == null || watchFiles.Length == 0) return;

    using var rebuildSignal = new SemaphoreSlim(0);
    var watchers = CreateWatchers(watchFiles, () => rebuildSignal.Release());

    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        var res = await msBuildService.RequestBuildAsync(projectPath, null, null, null, cancellationToken);
        if (!res.Success)
        {
          await editorService.DisplayError("Watch: Build failed.");
          await rebuildSignal.WaitAsync(cancellationToken);
          continue;
        }

        // 2. Start
        var debugSession = await debugOrchestrator.StartServerDebugSessionAsync(projectPath, sessionId, new(projectPath, null, null, ""), debugStrategyFactory.CreateStandardLaunchStrategy(null), cancellationToken);
        await editorService.RequestStartDebugSession("127.0.0.1", debugSession.Port);

        // 3. The Race
        var fileChangeTask = rebuildSignal.WaitAsync(cancellationToken);
        await Task.WhenAny(debugSession.Completion, fileChangeTask);

        // 4. DECISION POINT: Check the file change signal first
        // We use Wait(0) to check the semaphore without blocking. 
        // If it returns true, a file change happened (even if the debugger also finished).
        var isFileChange = fileChangeTask.IsCompleted || rebuildSignal.Wait(0, cancellationToken);

        if (isFileChange)
        {
          await editorService.DisplayMessage("Change detected. Rebuilding...");

          // Stop the session and wait for cleanup to avoid the SocketException
          await debugOrchestrator.StopDebugSessionAsync(projectPath);

          // Drain any extra signals (debouncing)
          while (rebuildSignal.Wait(0, cancellationToken)) { }
        }
        else
        {
          // No file change detected, so the debugger must have finished naturally
          await editorService.DisplayMessage("Debug session ended. Stopping watch.");
          return;
        }
      }
    }
    finally
    {
      foreach (var w in watchers) w.Dispose();
      await debugOrchestrator.StopDebugSessionAsync(projectPath);
    }
  }

  private List<FileSystemWatcher> CreateWatchers(string[] files, Action onChange)
  {
    var directories = files
        .Select(Path.GetDirectoryName)
        .Where(d => d != null)
        .Distinct()!;

    var watchers = new List<FileSystemWatcher>();
    foreach (var dir in directories)
    {
      var watcher = new FileSystemWatcher(dir!)
      {
        Filter = "*.*",
        EnableRaisingEvents = true
      };

      var lastTrigger = DateTime.MinValue;
      void Handler(object s, FileSystemEventArgs e)
      {
        if (files.Contains(e.FullPath, StringComparer.OrdinalIgnoreCase) && (DateTime.Now - lastTrigger).TotalMilliseconds > 200)
        {
          lastTrigger = DateTime.Now;
          onChange();
        }
      }

      watcher.Changed += Handler;
      watcher.Created += Handler;
      watcher.Renamed += (s, e) => Handler(s, e);

      watchers.Add(watcher);
    }
    return watchers;
  }
}