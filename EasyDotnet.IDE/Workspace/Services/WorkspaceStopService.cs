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
    var allNames = sessionRegistry.GetAllRunningNames();

    if (allNames.Count == 0)
    {
      await editorService.DisplayError("No running projects");
      return;
    }

    var running = sessionRegistry.GetRunningProcesses();

    if (running.Count == 0)
    {
      await editorService.DisplayError("Projects are still starting, please wait");
      return;
    }

    var target = running.Count == 1
        ? running[0]
        : await PickProcessAsync(running, ct);

    if (target is null)
      return;

    KillProcess(target);
  }

  private async Task<RunningProcessEntry?> PickProcessAsync(
      IReadOnlyList<RunningProcessEntry> processes,
      CancellationToken ct)
  {
    var choices = processes
        .Select(p => new PickerChoice<RunningProcessEntry>(p.SessionKey, p.ProjectName, p))
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