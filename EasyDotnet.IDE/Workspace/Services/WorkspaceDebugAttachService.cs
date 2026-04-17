using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Editor;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Picker.Models;
using EasyDotnet.IDE.Services;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceDebugAttachService(
    WorkspaceSessionRegistry sessionRegistry,
    IEditorService editorService,
    IDebugOrchestrator debugOrchestrator,
    IDebugStrategyFactory debugStrategyFactory,
    IProgressScopeFactory progressScopeFactory,
    ILogger<WorkspaceDebugAttachService> logger)
{
  private abstract record AttachTarget(string Label);
  private sealed record ServerAttachTarget(string Label, RunningProcessEntry Entry) : AttachTarget(Label);
  private sealed record ExternalAttachTarget(string ProcessName, int Pid, string MainModule) : AttachTarget(ProcessName);
  private sealed record SeparatorTarget(string Label) : AttachTarget(Label);

  public async Task DebugAttachAsync(CancellationToken ct)
  {
    try
    {
      var serverOwned = sessionRegistry.GetRunningProcesses()
          .Where(p => !debugOrchestrator.HasActiveSession(p.ProjectFullPath))
          .ToList();
      var external = DiscoverExternalProcesses([.. sessionRegistry.GetRunningProcesses().Select(p => p.Pid)])
          .Where(p => !debugOrchestrator.HasActiveSession($"pid:{p.Pid}"))
          .ToList();

      if (serverOwned.Count == 0 && external.Count == 0)
      {
        await editorService.DisplayError("No running .NET processes found.");
        return;
      }

      var choices = BuildChoices(serverOwned, external);

      var selected = await editorService.RequestPickerAsync(
          "Select process to debug",
          choices,
          ct: ct);

      if (selected is null or SeparatorTarget)
      {
        return;
      }

      await AttachToTargetAsync(selected, ct);
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

  private static List<ExternalAttachTarget> DiscoverExternalProcesses(HashSet<int> excludePids)
  {
    var currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
    var results = new List<ExternalAttachTarget>();

    foreach (var pid in DiagnosticsClient.GetPublishedProcesses())
    {
      if (excludePids.Contains(pid) || pid == currentPid)
      {
        continue;
      }

      string processName;
      string mainModule;
      try
      {
        var proc = System.Diagnostics.Process.GetProcessById(pid);
        processName = proc.ProcessName;
        try { mainModule = proc.MainModule?.FileName ?? string.Empty; }
        catch { mainModule = string.Empty; }
      }
      catch { continue; }

      if (IsSdkOrToolingProcess(processName, mainModule))
      {
        continue;
      }

      results.Add(new ExternalAttachTarget(processName, pid, mainModule));
    }

    return results;
  }

  private static bool IsSdkOrToolingProcess(string processName, string mainModule)
  {
    if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (processName.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    string[] knownTooling = ["VBCSCompiler", "csc", "vbc", "MSBuild", "dotnet-watch", "netcoredbg"];
    if (knownTooling.Contains(processName, StringComparer.OrdinalIgnoreCase))
    {
      return true;
    }

    if (mainModule.Contains("/dotnet/sdk/", StringComparison.OrdinalIgnoreCase) ||
        mainModule.Contains(@"\dotnet\sdk\", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    return string.IsNullOrEmpty(mainModule);
  }

  private static PickerChoice<AttachTarget>[] BuildChoices(
      IReadOnlyList<RunningProcessEntry> serverOwned,
      List<ExternalAttachTarget> external)
  {
    var choices = new List<PickerChoice<AttachTarget>>();

    foreach (var p in serverOwned)
    {
      var target = new ServerAttachTarget($"{p.ProjectName} (PID: {p.Pid})", p);
      choices.Add(new PickerChoice<AttachTarget>(p.SessionKey, target.Label, target));
    }

    if (serverOwned.Count > 0 && external.Count > 0)
    {
      var sep = new SeparatorTarget("--- Other .NET processes ---");
      choices.Add(new PickerChoice<AttachTarget>("__sep__", sep.Label, sep));
    }

    var nameGroups = external.GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    foreach (var p in external)
    {
      var hasDuplicate = nameGroups[p.ProcessName] > 1;
      var display = hasDuplicate
          ? $"{p.ProcessName} (PID: {p.Pid}) [{p.MainModule}]"
          : $"{p.ProcessName} (PID: {p.Pid})";
      choices.Add(new PickerChoice<AttachTarget>($"ext:{p.Pid}", display, p));
    }

    return [.. choices];
  }

  private async Task AttachToTargetAsync(AttachTarget target, CancellationToken ct)
  {
    var (sessionKey, pid, label, cwd) = target switch
    {
      ServerAttachTarget s => (s.Entry.ProjectFullPath, s.Entry.Pid, s.Label, Path.GetDirectoryName(s.Entry.ProjectFullPath)),
      ExternalAttachTarget e => ($"pid:{e.Pid}", e.Pid, e.Label, Path.GetDirectoryName(e.MainModule)),
      _ => throw new InvalidOperationException("Unexpected attach target type")
    };

    using var progress = progressScopeFactory.Create(
        "Debug Attach",
        $"Connecting debugger to {label}...");

    var strategy = debugStrategyFactory.CreateStandardAttachStrategy(pid, cwd);
    var session = await debugOrchestrator.StartClientDebugSessionAsync(sessionKey, strategy, ct);

    await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
    await session.ProcessStarted;
  }
}