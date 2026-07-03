using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Picker.Models;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceStopService(
    WorkspaceSessionRegistry sessionRegistry,
    IEditorService editorService,
    ILogger<WorkspaceStopService> logger)
{
  public async Task StopAsync(CancellationToken ct)
  {
    var allSessions = sessionRegistry.GetAllRunningSessions();

    if (allSessions.Count == 0)
    {
      await editorService.DisplayError("No running projects");
      return;
    }

    // Items with parents stem from aspire
    var processes = sessionRegistry.GetRunningProcesses().Where(p => p.ParentKey is null).ToList();

    var processKeys = processes.Select(p => p.SessionKey).ToHashSet();
    var debugSessions = sessionRegistry.GetRunningDebugSessions()
        .Where(d => !processKeys.Contains(d.SessionKey));

    var targets = processes
        .Select(p => (StopTarget)new ProcessStopTarget(p))
        .Concat(debugSessions.Select(d => (StopTarget)new DebugStopTarget(d.SessionKey, d.ProjectName, d.DebugSessionId)))
        .ToList();

    if (targets.Count == 0)
    {
      await editorService.DisplayError("Projects are still starting, please wait");
      return;
    }

    var target = targets.Count == 1
        ? targets[0]
        : await PickTargetAsync(targets, ct);

    if (target is null)
      return;

    switch (target)
    {
      case ProcessStopTarget p:
        // Stop children first (e.g. Aspire resources) so killing the parent (AppHost) doesn't orphan them
        foreach (var child in sessionRegistry.GetChildProcesses(p.Entry.SessionKey))
        {
          KillProcess(child);
        }
        KillProcess(p.Entry);
        break;

      case DebugStopTarget d:
        await editorService.RequestTerminateDebugSession(d.DebugSessionId);
        break;
    }
  }

  private abstract record StopTarget(string SessionKey, string ProjectName);
  private sealed record ProcessStopTarget(RunningProcessEntry Entry) : StopTarget(Entry.SessionKey, Entry.ProjectName);
  private sealed record DebugStopTarget(string SessionKey, string ProjectName, int DebugSessionId) : StopTarget(SessionKey, ProjectName);

  private async Task<StopTarget?> PickTargetAsync(
      IReadOnlyList<StopTarget> targets,
      CancellationToken ct)
  {
    var choices = targets
        .Select(t => new PickerChoice<StopTarget>(t.SessionKey, t.ProjectName, t))
        .ToArray();

    return await editorService.RequestPickerAsync("Select project to stop", choices, ct: ct);
  }

  private void KillProcess(RunningProcessEntry entry)
  {
    try
    {
      var process = System.Diagnostics.Process.GetProcessById(entry.Pid);
      process.Kill(entireProcessTree: true);
      logger.LogInformation("Killed process {ProjectName} (PID {Pid})", entry.ProjectName, entry.Pid);
    }
    catch (ArgumentException)
    {
      logger.LogWarning("Process {ProjectName} (PID {Pid}) was already gone", entry.ProjectName, entry.Pid);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to kill process {ProjectName} (PID {Pid})", entry.ProjectName, entry.Pid);
    }
  }
}