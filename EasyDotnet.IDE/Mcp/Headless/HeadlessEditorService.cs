using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Models.Client.Prompt;
using EasyDotnet.IDE.Models.Client.Quickfix;
using EasyDotnet.IDE.Picker.Models;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Mcp.Headless;

/// <summary>
/// <see cref="IEditorService"/> for hosts without an editor. Messages go to the log,
/// progress and quickfix lists are dropped, and anything that needs a user or an editor
/// to act fails with <see cref="HeadlessInteractionException"/> so callers pass explicit
/// arguments instead.
/// </summary>
public sealed class HeadlessEditorService(ILogger<HeadlessEditorService> logger) : IEditorService
{
  public Task CloseQuickFixList() => Task.CompletedTask;

  public Task DisplayError(string message)
  {
    logger.LogError("{Message}", message);
    return Task.CompletedTask;
  }

  public Task DisplayMessage(string message)
  {
    logger.LogInformation("{Message}", message);
    return Task.CompletedTask;
  }

  public Task DisplayWarning(string message)
  {
    logger.LogWarning("{Message}", message);
    return Task.CompletedTask;
  }

  public Task<bool> RequestConfirmation(string prompt, bool defaultValue) => throw Interaction(prompt);
  public Task<SelectionOption[]?> RequestMultiSelection(string prompt, SelectionOption[] choices) => throw Interaction(prompt);
  public Task<bool> RequestOpenBuffer(string path, int? line = null) => Task.FromResult(false);
  public Task<int> RequestRunCommandAsync(RunCommand command, CancellationToken ct = default) => throw Interaction($"run {command.Executable}");
  public Task<Guid> StartRunProjectAsync(RunProjectRequest request, CancellationToken ct = default) => throw Interaction("run a project");
  public Task<Guid> StartRunCommandAsync(RunCommand command, CancellationToken ct = default) => throw Interaction($"run {command.Executable}");
  public Task<SelectionOption?> RequestSelection(string prompt, SelectionOption[] choices, string? defaultSelectionId = null) => throw Interaction(prompt);
  public Task<bool> RequestSetBreakpoint(string path, int lineNumber) => Task.FromResult(false);
  public Task<int> RequestStartDebugSession(string host, int port) => throw Interaction("start a debug session");
  public Task<string?> RequestString(string prompt, string? defaultValue) => throw Interaction(prompt);
  public Task<bool> RequestTerminateDebugSession(int sessionId) => Task.FromResult(false);
  public Task SendProgressEnd(string token) => Task.CompletedTask;
  public Task SendProgressStart(string token, string title, string message, int? percentage = null) => Task.CompletedTask;
  public Task SendProgressUpdate(string token, string? message, int? percentage = null) => Task.CompletedTask;
  public Task SetQuickFixList(QuickFixItem[] quickFixItems) => Task.CompletedTask;
  public Task SetQuickFixListSilent(QuickFixItem[] quickFixItems) => Task.CompletedTask;
  public Task<bool> BuildProject(string projectPath, CancellationToken cancellationToken) => throw Interaction($"build {projectPath}");
  public Task<bool> ApplyWorkspaceEdit(WorkspaceEdit edit) => Task.FromResult(false);

  public Task<T?> RequestPickerAsync<T>(string prompt, PickerChoice<T>[] choices, Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null, CancellationToken ct = default) =>
      throw Interaction(prompt);

  public Task<T[]?> RequestMultiPickerAsync<T>(string prompt, PickerChoice<T>[] choices, Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null, CancellationToken ct = default) =>
      throw Interaction(prompt);

  public Task<T?> RequestLivePickerAsync<T>(string prompt, Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory, Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null, CancellationToken ct = default) =>
      throw Interaction(prompt);

  public Task<T[]?> RequestMultiLivePickerAsync<T>(string prompt, Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory, Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null, CancellationToken ct = default) =>
      throw Interaction(prompt);

  private static HeadlessInteractionException Interaction(string what) =>
      new($"'{what}' needs an interactive editor, which this host does not have. Pass the value explicitly instead.");
}

public sealed class HeadlessInteractionException(string message) : InvalidOperationException(message);