using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Editor;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Picker.Models;
using EasyDotnet.IDE.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceDebugAttachService(
    RunningProcessRegistry runningProcessRegistry,
    IEditorService editorService,
    IDebugOrchestrator debugOrchestrator,
    IDebugStrategyFactory debugStrategyFactory,
    IProgressScopeFactory progressScopeFactory,
    ILogger<WorkspaceDebugAttachService> logger)
{
  public async Task DebugAttachAsync(CancellationToken ct)
  {
    try
    {
      var processes = runningProcessRegistry.GetAll();
      if (processes.Count == 0)
      {
        await editorService.DisplayError("No running processes to connect to. Start a project with 'Dotnet run' first.");
        return;
      }

      var choices = processes
          .Select(p => new PickerChoice<RunningProcessEntry>(p.SessionKey, $"{p.ProjectName} (PID: {p.Pid})", p))
          .ToArray();

      var selected = await editorService.RequestPickerAsync(
          "Select process to debug",
          choices,
          ct: ct);

      if (selected is null)
        return;

      using var progress = progressScopeFactory.Create(
          "Debug Attach",
          $"Connecting debugger to {selected.ProjectName} (PID: {selected.Pid})...");

      var startRequest = new DebuggerStartRequest(
          selected.ProjectFullPath,
          selected.TargetFramework,
          "Debug",
          null);

      var strategy = debugStrategyFactory.CreateStandardAttachStrategy(selected.Pid);

      var session = await debugOrchestrator.StartClientDebugSessionAsync(
          selected.ProjectFullPath, startRequest, strategy, ct);

      await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
      await session.ProcessStarted;
    }
    catch (OperationCanceledException)
    {
      logger.LogInformation("Debug attach cancelled");
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error during debug attach");
      await editorService.DisplayError($"Debug attach failed: {ex.Message}");
    }
  }
}