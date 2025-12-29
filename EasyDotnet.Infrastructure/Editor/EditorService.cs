using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Client;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Editor;

public class EditorService(IEditorProcessManagerService editorProcessManagerService, JsonRpc jsonRpc) : IEditorService
{
  public async Task DisplayError(string message) =>
      await jsonRpc.NotifyWithParameterObjectAsync("displayError", new DisplayMessage(message));

  public async Task DisplayWarning(string message) =>
    await jsonRpc.NotifyWithParameterObjectAsync("displayWarning", new DisplayMessage(message));

  public async Task DisplayMessage(string message) =>
    await jsonRpc.NotifyWithParameterObjectAsync("displayMessage", new DisplayMessage(message));

  public async Task<bool> RequestOpenBuffer(string path) => await jsonRpc.InvokeWithParameterObjectAsync<bool>("openBuffer", new OpenBufferRequest(path));
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

  public async Task<Guid> RequestRunCommand(RunCommand command)
  {
    var guid = editorProcessManagerService.RegisterJob();
    try
    {
      _ = await jsonRpc.InvokeWithParameterObjectAsync<RunCommandResponse>("runCommand", new TrackedJob(guid, command));
    }
    catch (RemoteInvocationException e)
    {
      editorProcessManagerService.SetFailedToStart(guid, e.Message);
    }

    return guid;
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

  public async Task CloseQuickFixList() => await jsonRpc.NotifyWithParameterObjectAsync("quickfix/close");
}