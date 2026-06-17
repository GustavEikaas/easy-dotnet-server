using System.Collections.Concurrent;
using System.Diagnostics;
using EasyDotnet.Aspire.Contracts;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Workspace.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Aspire;

public sealed class AspireRunService(
    WorkspaceBuildHostManager buildHostManager,
    WorkspaceSessionRegistry sessionRegistry,
    INotificationService notificationService,
    IEditorService editorService,
    IEditorProcessManagerService processManager,
    ILogger<AspireRunService> logger)
{
  private readonly ConcurrentDictionary<string, int> _pidsByRunId = new();

  public async Task<int> RunAsync(RunManagedResourceRequest request, Func<int, Task> reportPid, CancellationToken ct)
  {
    var project = await ResolveProjectAsync(request.ProjectPath, ct) ?? throw new InvalidOperationException($"Could not evaluate project '{request.ProjectPath}' for run");

    logger.LogInformation("Running Aspire resource {RunId} from {Project} ({Tfm})",
        request.RunId, project.ProjectName, project.TargetFramework);

    var sessionKey = SessionKey(request.RunId);
    var isAppHost = request.RunId == AspireRunIds.AppHost;
    var parentKey = isAppHost ? null : SessionKey(AspireRunIds.AppHost);

    sessionRegistry.TryClaim(sessionKey, project.ProjectName, parentKey: parentKey);
    await NotifyRunningSessionsAsync();

    var runRequest = new RunProjectRequest(
        project.Raw,
        // DCP already resolved launch-profile values into the env[]
        LaunchProfile: null,
        AdditionalArguments: request.Args?.ToArray(),
        EnvironmentVariables: request.EnvironmentVariables,
        OnPidReceived: pid =>
        {
          _pidsByRunId[request.RunId] = pid;
          sessionRegistry.SetProcessInfo(sessionKey, new RunningProcessEntry(
              sessionKey, project.ProjectName, project.ProjectFullPath, project.TargetFramework, pid, parentKey));
          _ = NotifyRunningSessionsAsync();
          if (reportPid is not null)
          {
            _ = reportPid(pid);
          }
        });

    try
    {
      var jobId = await editorService.StartRunProjectAsync(runRequest, ct);
      return await processManager.WaitForExitAsync(jobId);
    }
    finally
    {
      _pidsByRunId.TryRemove(request.RunId, out _);
      sessionRegistry.Release(sessionKey);
      await NotifyRunningSessionsAsync();
    }
  }

  /// <summary>
  /// Terminates the process backing <paramref name="runId"/> (DCP <c>DELETE /run_session</c>).
  /// </summary>
  public void Stop(string runId)
  {
    if (!_pidsByRunId.TryGetValue(runId, out var pid))
    {
      return;
    }

    try
    {
      using var process = Process.GetProcessById(pid);
      process.Kill(entireProcessTree: true);
      logger.LogInformation("Stopped Aspire resource {RunId} (PID {Pid})", runId, pid);
    }
    catch (ArgumentException)
    {
      logger.LogWarning("Aspire resource {RunId} (PID {Pid}) was already gone", runId, pid);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to stop Aspire resource {RunId} (PID {Pid})", runId, pid);
    }
  }

  private static string SessionKey(string runId) => $"aspire:{runId}";

  private Task NotifyRunningSessionsAsync() =>
      notificationService.NotifyRunningProcessesChangedAsync(
          [.. sessionRegistry.GetAllRunningSessions().Select(s => new RunningSessionInfo(s.ProjectName, s.IsDebugging))]);

  private async Task<ValidatedDotnetProject?> ResolveProjectAsync(string projectPath, CancellationToken ct)
  {
    //HACK: assume first valid tfm (how the heck is this supposed to work??)
    var request = new GetProjectPropertiesBatchRequest([projectPath], Configuration: null, Platform: null, ComputeRunArguments: true);
    await foreach (var result in buildHostManager.GetProjectPropertiesBatchAsync(request, ct))
    {
      if (result is { Success: true, Project: not null })
      {
        return result.Project;
      }
    }
    return null;
  }
}