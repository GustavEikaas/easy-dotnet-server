using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Client;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Services;

public sealed record SetBreakpointRequest(string Path, int LineNumber);
public sealed record OpenBufferRequest(string Path);
public sealed record PromptString(string Prompt, string? DefaultValue);
public sealed record PromptConfirmRequest(string Prompt, bool DefaultValue);
public sealed record PromptSelectionRequest(string Prompt, SelectionOption[] Choices, string? DefaultSelectionId);
public sealed record PromptMultiSelectionRequest(string Prompt, SelectionOption[] Choices);
public sealed record StartDebugSessionRequest(string Host, int Port);
public sealed record TerminateDebugSessionRequest(int SessionId);
public sealed record TrackedJob(Guid JobId, RunCommand Command);

public class ClientService(JsonRpc rpc, IEditorProcessManagerService editorProcessManagerService) : IClientService
{
  public bool IsInitialized { get; set; }
  public bool UseVisualStudio { get; set; }
  public ProjectInfo? ProjectInfo { get; set; }
  public ClientInfo? ClientInfo { get; set; }
  public ClientOptions? ClientOptions { get; set; }

  public void ThrowIfNotInitialized()
  {
    if (!IsInitialized)
    {
      throw new Exception("Client has not initialized yet");
    }
  }

  public async Task<bool> RequestOpenBuffer(string path) => await rpc.InvokeWithParameterObjectAsync<bool>("openBuffer", new OpenBufferRequest(path));
  public async Task<bool> RequestSetBreakpoint(string path, int lineNumber) => await rpc.InvokeWithParameterObjectAsync<bool>("setBreakpoint", new SetBreakpointRequest(path, lineNumber));
  public async Task<bool> RequestConfirmation(string prompt, bool defaultValue) => await rpc.InvokeWithParameterObjectAsync<bool>("promptConfirm", new PromptConfirmRequest(prompt, defaultValue));
  public async Task<string?> RequestString(string prompt, string? defaultValue) => await rpc.InvokeWithParameterObjectAsync<string?>("promptString", new PromptString(prompt, defaultValue));

  public async Task<SelectionOption?> RequestSelection(string prompt, SelectionOption[] choices, string? defaultSelectionId = null)
  {
    var request = new PromptSelectionRequest(prompt, choices, defaultSelectionId);
    var selectedId = await rpc.InvokeWithParameterObjectAsync<string?>("promptSelection", request);
    return selectedId == null ? null : choices.FirstOrDefault(option => option.Id == selectedId);
  }

  public async Task<SelectionOption[]?> RequestMultiSelection(string prompt, SelectionOption[] choices)
  {
    var request = new PromptMultiSelectionRequest(prompt, choices);
    var selectedIds = await rpc.InvokeWithParameterObjectAsync<string[]?>("promptMultiSelection", request);
    return selectedIds == null ? null : [.. choices.Where(option => selectedIds.Contains(option.Id))];
  }

  public async Task<Guid> RequestRunCommand(RunCommand command)
  {
    var guid = editorProcessManagerService.RegisterJob();
    await rpc.InvokeWithParameterObjectAsync<RunCommandResponse>("runCommand", new TrackedJob(guid, command));
    return guid;
  }

  public async Task<int> RequestStartDebugSession(string host, int port)
  {
    var request = new StartDebugSessionRequest(host, port);
    return await rpc.InvokeWithParameterObjectAsync<int>("startDebugSession", request);
  }

  public async Task<bool> RequestTerminateDebugSession(int sessionId)
  {
    var request = new TerminateDebugSessionRequest(sessionId);
    return await rpc.InvokeWithParameterObjectAsync<bool>("terminateDebugSession", request);
  }

  public async Task SendProgressStart(string token, string title, string message, int? percentage = null)
  {
    var progress = new ProgressParams(token, new ProgressValue("begin", title, message, percentage));
    await rpc.NotifyWithParameterObjectAsync("$/progress", progress);
  }

  public async Task SendProgressUpdate(string token, string? message, int? percentage = null)
  {
    var progress = new ProgressParams(token, new ProgressValue("report", Title: null, message, percentage));
    await rpc.NotifyWithParameterObjectAsync("$/progress", progress);
  }

  public async Task SendProgressEnd(string token)
  {
    var progress = new ProgressParams(token, new ProgressValue("end", Title: null, Message: null, Percentage: null));
    await rpc.NotifyWithParameterObjectAsync("$/progress", progress);
  }
}

public sealed record ProgressParams(string Token, ProgressValue Value);

public sealed record ProgressValue(
    string Kind,
    string? Title = null,
    string? Message = null,
    int? Percentage = null,
    bool? Cancellable = null
);