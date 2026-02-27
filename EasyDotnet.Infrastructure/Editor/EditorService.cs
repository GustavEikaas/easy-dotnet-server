using System.CommandLine.Parsing;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Client;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Editor;

public class EditorService(
  IEditorProcessManagerService editorProcessManagerService,
  IStartupHookService startupHookService,
  IMsBuildService buildService,
  IClientService clientService,
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

  public async Task<Guid> RequestRunCommand(RunCommand command)
  {
    var guid = editorProcessManagerService.RegisterJob(TerminalSlot.Managed);
    try
    {
      _ = await jsonRpc.InvokeWithParameterObjectAsync<RunCommandResponse>("runCommandManaged", new TrackedJob(guid, command));
    }
    catch (RemoteInvocationException e)
    {
      editorProcessManagerService.SetFailedToStart(guid, TerminalSlot.Managed, e.Message);
    }
    return guid;
  }

  public async Task<int> RequestRunCommandAndWaitAsync(RunCommand command, CancellationToken ct = default)
  {
    var guid = await RequestRunCommand(command);
    return await editorProcessManagerService.WaitForExitAsync(guid, TerminalSlot.Managed);
  }

  public async Task<int> RequestRunProjectAsync(RunProjectRequest request, CancellationToken ct = default)
  {
    var guid = editorProcessManagerService.RegisterJob(TerminalSlot.LongRunning);
    await using var session = startupHookService.CreateSession(request.EnvironmentVariables);

    var command = BuildRunCommand(request, session.EnvironmentVariables);
    var rpcMethod = clientService.HasExternalTerminal ? "runCommandExternal" : "runCommandManaged";

    try
    {
      _ = await jsonRpc.InvokeWithParameterObjectAsync<RunCommandResponse>(
          rpcMethod, new TrackedJob(guid, command), ct);
    }
    catch (RemoteInvocationException e)
    {
      editorProcessManagerService.SetFailedToStart(guid, TerminalSlot.LongRunning, e.Message);
      throw;
    }

    var pid = await session.WaitForPidAsync(ct);
    session.Resume();

    if (clientService.HasExternalTerminal)
    {
      _ = MonitorExternalProcessAsync(pid, guid, ct);
    }

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

  public async Task CloseQuickFixList() => await jsonRpc.NotifyWithParameterObjectAsync("quickfix/close");

  public async Task<bool> BuildProject(string projectPath, CancellationToken cancellationToken)
  {
    using var progress = new ProgressScope(this, "Building", $"Building {Path.GetFileNameWithoutExtension(projectPath)}");
    var res = await buildService.RequestBuildAsync(projectPath, null, null, null, cancellationToken);
    if (res.Success)
    {
      return true;
    }
    progress.Report("Build failed", 100);
    var errors = res.Errors.Select(x => new QuickFixItem(x.FilePath, x.LineNumber, x.ColumnNumber, x.Message ?? "ERR", QuickFixItemType.Error));
    await SetQuickFixList([.. errors]);
    return false;
  }

  private static RunCommand BuildRunCommand(RunProjectRequest request, Dictionary<string, string> hookEnv)
  {
    var args = new List<string> { request.Project.TargetPath! };

    if (request.LaunchProfile?.CommandLineArgs is not null)
    {
      args.AddRange(CommandLineParser.SplitCommandLine(request.LaunchProfile.CommandLineArgs));
    }

    if (request.AdditionalArguments is { Length: > 0 })
    {
      args.AddRange(request.AdditionalArguments);
    }

    var env = new Dictionary<string, string>(request.EnvironmentVariables ?? [], StringComparer.OrdinalIgnoreCase);

    foreach (var kvp in request.LaunchProfile?.EnvironmentVariables ?? [])
    {
      env[kvp.Key] = kvp.Value;
    }

    foreach (var kvp in hookEnv)
    {
      env[kvp.Key] = kvp.Value;
    }

    return new RunCommand(
        "dotnet",
        [.. args],
        request.LaunchProfile?.WorkingDirectory ?? request.Project.ProjectDir ?? ".",
        env);
  }

  private async Task MonitorExternalProcessAsync(int pid, Guid jobId, CancellationToken ct)
  {
    try
    {
      using var process = System.Diagnostics.Process.GetProcessById(pid);
      process.EnableRaisingEvents = true;
      await process.WaitForExitAsync(ct);

      editorProcessManagerService.CompleteJob(jobId, process.ExitCode);

      await jsonRpc.NotifyAsync("notifyExternalTerminalExited");
    }
    catch (ArgumentException)
    {
      editorProcessManagerService.CompleteJob(jobId, -1);

      await jsonRpc.NotifyAsync("notifyExternalTerminalExited");
    }
  }
}