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

/// <summary>
/// Runs an AppHost / DCP resource on behalf of the spawned Aspire host, reusing the
/// same machinery as <c>WorkspaceService</c>: evaluate the project, let
/// <c>WorkspaceRunCommandBuilder</c> (via <see cref="IEditorService.StartRunProjectAsync"/>)
/// resolve the real run command, relay it to Neovim, and block until exit. Each run is
/// registered in the <see cref="WorkspaceSessionRegistry"/> so it shows in the running
/// UI and can be stopped, and tracked by runId so DCP's <c>DELETE /run_session</c> can
/// terminate it.
/// </summary>
public sealed class AspireRunService(
    WorkspaceBuildHostManager buildHostManager,
    WorkspaceSessionRegistry sessionRegistry,
    INotificationService notificationService,
    IEditorService editorService,
    IEditorProcessManagerService processManager,
    ILogger<AspireRunService> logger)
{
  private readonly ConcurrentDictionary<string, int> _pidsByRunId = new();

  public async Task<int> RunAsync(RunManagedResourceRequest request, Func<int, Task>? reportPid = null, CancellationToken ct = default)
  {
    var project = await ResolveProjectAsync(request.ProjectPath, ct)
        ?? throw new InvalidOperationException($"Could not evaluate project '{request.ProjectPath}' for run");

    logger.LogInformation("Running Aspire resource {RunId} from {Project} ({Tfm})",
        request.RunId, project.ProjectName, project.TargetFramework);

    var sessionKey = SessionKey(request.RunId);
    // The AppHost is the parent session; every resource is a child of it, so the stop
    // picker shows just the Aspire app and stopping it cascades to the resources.
    var isAppHost = request.RunId == AspireRunIds.AppHost;
    var parentKey = isAppHost ? null : SessionKey(AspireRunIds.AppHost);

    sessionRegistry.TryClaim(sessionKey, project.ProjectName, parentKey: parentKey);
    await NotifyRunningSessionsAsync();

    // No launch profile: DCP already resolved launch-profile values into the env[]
    // it sent, which we pass through as the authoritative environment.
    var runRequest = new RunProjectRequest(
        project.Raw,
        LaunchProfile: null,
        AdditionalArguments: request.Args?.ToArray(),
        EnvironmentVariables: request.EnvironmentVariables,
        OnPidReceived: pid =>
        {
          _pidsByRunId[request.RunId] = pid;
          sessionRegistry.SetProcessInfo(sessionKey, new RunningProcessEntry(
              sessionKey, project.ProjectName, project.ProjectFullPath, project.TargetFramework, pid, parentKey));
          _ = NotifyRunningSessionsAsync();
          // Report the pid back to the Aspire host so it can tell DCP (dashboard pid).
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
  /// Killing it makes the in-flight <see cref="RunAsync"/> complete, which releases the
  /// session and lets the Aspire host emit <c>sessionTerminated</c>.
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
    // project_path arrives without a target framework; take the first successful
    // evaluation (single-TFM resources are the norm).
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