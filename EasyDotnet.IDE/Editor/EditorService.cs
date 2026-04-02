using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Models.Client.Debugger;
using EasyDotnet.IDE.Models.Client.Progress;
using EasyDotnet.IDE.Models.Client.Prompt;
using EasyDotnet.IDE.Models.Client.Quickfix;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Editor;

public class EditorService(
  IEditorProcessManagerService editorProcessManagerService,
  IStartupHookService startupHookService,
  IBuildHostManager buildHostManager,
  IClientService clientService,
  IAppWrapperManager appWrapperManager,
  JsonRpc jsonRpc) : IEditorService
{
  public async Task DisplayError(string message) =>
      await jsonRpc.NotifyWithParameterObjectAsync("displayError", new DisplayMessage(message));

  public async Task DisplayWarning(string message) =>
    await jsonRpc.NotifyWithParameterObjectAsync("displayWarning", new DisplayMessage(message));

  public async Task DisplayMessage(string message) =>
    await jsonRpc.NotifyWithParameterObjectAsync("displayMessage", new DisplayMessage(message));

  public async Task<bool> RequestOpenBuffer(string path, int? line = null) => await jsonRpc.InvokeWithParameterObjectAsync<bool>("openBuffer", new OpenBufferRequest(path, line));
  public async Task<bool> RequestSetBreakpoint(string path, int lineNumber) => await jsonRpc.InvokeWithParameterObjectAsync<bool>("setBreakpoint", new SetBreakpointRequest(path, lineNumber));
  public async Task<bool> RequestConfirmation(string prompt, bool defaultValue) => await jsonRpc.InvokeWithParameterObjectAsync<bool>("promptConfirm", new PromptConfirmRequest(prompt, defaultValue));
  public async Task<string?> RequestString(string prompt, string? defaultValue) => await jsonRpc.InvokeWithParameterObjectAsync<string?>("promptString", new PromptString(prompt, defaultValue));

  public async Task<SelectionOption?> RequestSelection(string prompt, SelectionOption[] choices, string? defaultSelectionId = null)
  {
    var request = new PromptSelectionRequest(prompt, choices, defaultSelectionId);
    var selectedId = await jsonRpc.InvokeWithParameterObjectAsync<string?>("promptSelection", request);
    return selectedId == null ? null : choices.FirstOrDefault(option => option.Id == selectedId);
  }

  public async Task<SelectionOption[]?> RequestMultiSelection(string prompt, SelectionOption[] choices)
  {
    var request = new PromptMultiSelectionRequest(prompt, choices);
    var selectedIds = await jsonRpc.InvokeWithParameterObjectAsync<string[]?>("promptMultiSelection", request);
    return selectedIds == null ? null : [.. choices.Where(option => selectedIds.Contains(option.Id))];
  }

  public async Task<int> RequestRunCommandAsync(RunCommand command, CancellationToken ct = default)
  {
    var guid = editorProcessManagerService.RegisterJob(TerminalSlot.Managed);
    try
    {
      _ = await jsonRpc.InvokeWithParameterObjectAsync<RunCommandResponse>(
          "runCommandManaged", new TrackedJob(guid, command), ct);
    }
    catch (RemoteInvocationException e)
    {
      editorProcessManagerService.SetFailedToStart(guid, TerminalSlot.Managed, e.Message);
      throw;
    }
    return await editorProcessManagerService.WaitForExitAsync(guid, TerminalSlot.Managed);
  }

  public async Task<int> RequestRunProjectAsync(RunProjectRequest request, CancellationToken ct = default)
  {
    var guid = editorProcessManagerService.RegisterJob(TerminalSlot.LongRunning);
    await using var session = startupHookService.CreateSession(request.EnvironmentVariables);

    var command = BuildRunCommand(request, session.EnvironmentVariables);

    try
    {
      if (clientService.HasExternalTerminal)
      {
        var wrapper = await appWrapperManager.GetOrSpawnAsync(ct);
        await wrapper.SendRunCommandAsync(guid, command, ct);
      }
      else
      {
        await jsonRpc.InvokeWithParameterObjectAsync("runCommandManaged", new TrackedJob(guid, command), ct);
      }
    }
    catch (Exception e)
    {
      editorProcessManagerService.SetFailedToStart(guid, TerminalSlot.LongRunning, e.Message);
      throw;
    }

    var pid = await session.WaitForPidAsync(ct);
    session.Resume();
    //TODO: open browser if applicable

    return await editorProcessManagerService.WaitForExitAsync(guid, TerminalSlot.LongRunning);
  }

  public async Task<int> RequestStartDebugSession(string host, int port)
  {
    var request = new StartDebugSessionRequest(host, port);
    return await jsonRpc.InvokeWithParameterObjectAsync<int>("startDebugSession", request);
  }

  public async Task<bool> RequestTerminateDebugSession(int sessionId)
  {
    var request = new TerminateDebugSessionRequest(sessionId);
    return await jsonRpc.InvokeWithParameterObjectAsync<bool>("terminateDebugSession", request);
  }

  public async Task SendProgressStart(string token, string title, string message, int? percentage = null)
  {
    var progress = new ProgressParams(token, new ProgressValue("begin", title, message, percentage));
    await jsonRpc.NotifyWithParameterObjectAsync("$/progress", progress);
  }

  public async Task SendProgressUpdate(string token, string? message, int? percentage = null)
  {
    var progress = new ProgressParams(token, new ProgressValue("report", Title: null, message, percentage));
    await jsonRpc.NotifyWithParameterObjectAsync("$/progress", progress);
  }

  public async Task SendProgressEnd(string token)
  {
    var progress = new ProgressParams(token, new ProgressValue("end", Title: null, Message: null, Percentage: null));
    await jsonRpc.NotifyWithParameterObjectAsync("$/progress", progress);
  }

  public async Task SetQuickFixList(QuickFixItem[] quickFixItems) => await jsonRpc.NotifyWithParameterObjectAsync("quickfix/set", quickFixItems);

  public async Task SetQuickFixListSilent(QuickFixItem[] quickFixItems) => await jsonRpc.NotifyWithParameterObjectAsync("quickfix/set-silent", quickFixItems);

  public async Task CloseQuickFixList() => await jsonRpc.NotifyWithParameterObjectAsync("quickfix/close");

  public async Task<bool> BuildProject(string projectPath, CancellationToken cancellationToken)
  {
    var name = Path.GetFileNameWithoutExtension(projectPath);
    List<BatchBuildResult> results;
    using (new ProgressScope(this, "Building", $"Building {name}"))
    {
      results = await buildHostManager
          .BatchBuildAsync(new BatchBuildRequest([projectPath], "Debug"), cancellationToken)
          .ToListAsync(cancellationToken);
    }

    var allDiagnostics = results
        .Where(r => r.Kind == BatchBuildResultKind.Finished)
        .SelectMany(r => r.Output?.Diagnostics ?? [])
        .ToList();

    var errors = allDiagnostics
        .Where(d => d.Severity == BuildDiagnosticSeverity.Error)
        .Select(d => new QuickFixItem(
            FileName: d.File ?? "",
            LineNumber: d.LineNumber,
            ColumnNumber: d.ColumnNumber,
            Text: string.IsNullOrEmpty(d.Code) ? d.Message ?? "" : $"[{d.Code}] {d.Message}",
            Type: QuickFixItemType.Error))
        .ToList();

    var warnings = allDiagnostics
        .Where(d => d.Severity == BuildDiagnosticSeverity.Warning)
        .Select(d => new QuickFixItem(
            FileName: d.File ?? "",
            LineNumber: d.LineNumber,
            ColumnNumber: d.ColumnNumber,
            Text: string.IsNullOrEmpty(d.Code) ? d.Message ?? "" : $"[{d.Code}] {d.Message}",
            Type: QuickFixItemType.Warning))
        .ToList();

    if (errors.Count > 0)
    {
      await SetQuickFixList([.. errors.Concat(warnings)]);
      return false;
    }

    if (warnings.Count > 0)
      await SetQuickFixListSilent([.. warnings]);

    return true;
  }

  private static RunCommand BuildRunCommand(RunProjectRequest request, Dictionary<string, string> hookEnv)
  {
    var args = new List<string> { request.Project.TargetPath! };

    if (request.LaunchProfile?.CommandLineArgs is not null)
    {
      args.AddRange(LaunchProfileUtils.ParseCommandLineArgs(request.LaunchProfile.CommandLineArgs, request.Project));
    }

    if (request.AdditionalArguments is { Length: > 0 })
    {
      args.AddRange(request.AdditionalArguments);
    }

    var env = LaunchProfileUtils.GetEnvironmentVariables(request.LaunchProfile);
    foreach (var kvp in hookEnv)
    {
      env[kvp.Key] = kvp.Value;
    }

    return new RunCommand("dotnet", [.. args], LaunchProfileUtils.ResolveCwd(request.LaunchProfile, request.Project), env);
  }
}