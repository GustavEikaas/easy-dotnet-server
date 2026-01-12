using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.MsBuild;
using EasyDotnet.IDE.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Watch;

public class WatchController(
    IEditorService editorService,
    IMsBuildService msBuildService,
    IDebugOrchestrator debugOrchestrator) : BaseController
{
  [JsonRpcMethod("msbuild/watch")]
  public async Task Watch(BuildRequest request, CancellationToken cancellationToken)
  {
    var sessionId = Guid.NewGuid().ToString();
    var watchFiles = await GetWatchListAsync(request.TargetPath, request.TargetFramework, request.ConfigurationOrDefault, cancellationToken);
    if (watchFiles == null || watchFiles.Length == 0) return;

    using var rebuildSignal = new SemaphoreSlim(0);
    var watchers = CreateWatchers(watchFiles, () => rebuildSignal.Release());

    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        // 1. Build
        var res = await msBuildService.RequestBuildAsync(request.TargetPath, request.TargetFramework, null, request.ConfigurationOrDefault, cancellationToken);
        if (!res.Success)
        {
          await editorService.DisplayError("Watch: Build failed.");
          await rebuildSignal.WaitAsync(cancellationToken);
          continue;
        }

        // 2. Start
        var debugSession = await debugOrchestrator.StartServerDebugSessionAsync(request.TargetPath, sessionId, new(request.TargetPath, request.TargetFramework, request.ConfigurationOrDefault, ""), cancellationToken);
        await editorService.RequestStartDebugSession("127.0.0.1", debugSession.Port);

        // 3. The Race
        var fileChangeTask = rebuildSignal.WaitAsync(cancellationToken);
        await Task.WhenAny(debugSession.Completion, fileChangeTask);

        // 4. DECISION POINT: Check the file change signal first
        // We use Wait(0) to check the semaphore without blocking. 
        // If it returns true, a file change happened (even if the debugger also finished).
        bool isFileChange = fileChangeTask.IsCompleted || rebuildSignal.Wait(0);

        if (isFileChange)
        {
          await editorService.DisplayMessage("Change detected. Rebuilding...");

          // Stop the session and wait for cleanup to avoid the SocketException
          await debugOrchestrator.StopDebugSessionAsync(request.TargetPath);

          // Drain any extra signals (debouncing)
          while (rebuildSignal.Wait(0)) { }
          continue;
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
      await debugOrchestrator.StopDebugSessionAsync(request.TargetPath);
    }
  }

  private async Task<string[]?> GetWatchListAsync(
      string projectPath,
      string? targetFramework,
      string configuration,
      CancellationToken cancellationToken)
  {
    var tempFile = Path.GetTempFileName();
    var watchTargetsFile = msBuildService.GetDotnetWatchTargets();

    if (string.IsNullOrEmpty(watchTargetsFile))
    {
      await editorService.DisplayError("Could not find DotNetWatch.targets");
      return null;
    }

    try
    {
      var args = new List<string>
            {
                "msbuild",
                "/t:GenerateWatchList",
                $"/p:_DotNetWatchListFile={tempFile}",
                "/p:DotNetWatchBuild=true",
                "/p:DesignTimeBuild=true",
                $"/p:CustomAfterMicrosoftCommonTargets={watchTargetsFile}",
                $"/p:CustomAfterMicrosoftCommonCrossTargetingTargets={watchTargetsFile}",
                $"/p:Configuration={configuration}",
                "/nologo",
                "/v:q",
                projectPath
            };

      if (!string.IsNullOrEmpty(targetFramework))
      {
        args.Insert(args.Count - 1, $"/p:TargetFramework={targetFramework}");
      }

      var psi = new ProcessStartInfo
      {
        FileName = "dotnet",
        Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      var process = Process.Start(psi);
      if (process == null)
      {
        await editorService.DisplayError("Failed to start MSBuild process");
        return null;
      }

      await process.WaitForExitAsync(cancellationToken);

      if (process.ExitCode != 0)
      {
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await editorService.DisplayError($"Failed to generate watch list: {error}");
        return null;
      }

      if (!File.Exists(tempFile))
      {
        await editorService.DisplayError("Watch list file was not created");
        return null;
      }

      // Parse JSON
      var json = await File.ReadAllTextAsync(tempFile, cancellationToken);
      var result = JsonSerializer.Deserialize<WatchListResult>(json);

      if (result?.Projects == null)
      {
        await editorService.DisplayError("Invalid watch list format");
        return null;
      }

      var files = result.Projects.Values
          .SelectMany(p => p.Files ?? [])
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToArray();

      await editorService.DisplayMessage($"Watching {files.Length} files");

      return files;
    }
    catch (Exception ex)
    {
      await editorService.DisplayError($"Error getting watch list: {ex.Message}");
      return null;
    }
    finally
    {
      if (File.Exists(tempFile))
      {
        File.Delete(tempFile);
      }
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

  private record WatchListResult(Dictionary<string, ProjectFiles>? Projects);
  private record ProjectFiles(List<string>? Files, List<StaticFile>? StaticFiles);
  private record StaticFile(string FilePath, string StaticWebAssetPath);
}