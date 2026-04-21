using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Models.Client.Prompt;
using EasyDotnet.IDE.Models.Client.Quickfix;
using EasyDotnet.IDE.Picker;
using EasyDotnet.IDE.Picker.Models;

namespace EasyDotnet.IDE.Interfaces;

public interface IEditorService
{
  Task CloseQuickFixList();
  Task DisplayError(string message);
  Task DisplayMessage(string message);
  Task DisplayWarning(string message);
  Task<bool> RequestConfirmation(string prompt, bool defaultValue);
  Task<SelectionOption[]?> RequestMultiSelection(string prompt, SelectionOption[] choices);
  Task<bool> RequestOpenBuffer(string path, int? line = null);
  Task<int> RequestRunCommandAsync(RunCommand command, CancellationToken ct = default);
  Task<Guid> StartRunProjectAsync(RunProjectRequest request, CancellationToken ct = default);
  Task<Guid> StartRunCommandAsync(RunCommand command, CancellationToken ct = default);
  Task<SelectionOption?> RequestSelection(string prompt, SelectionOption[] choices, string? defaultSelectionId = null);
  Task<bool> RequestSetBreakpoint(string path, int lineNumber);
  Task<int> RequestStartDebugSession(string host, int port);
  Task<string?> RequestString(string prompt, string? defaultValue);
  Task<bool> RequestTerminateDebugSession(int sessionId);
  Task SendProgressEnd(string token);
  Task SendProgressStart(string token, string title, string message, int? percentage = null);
  Task SendProgressUpdate(string token, string? message, int? percentage = null);
  Task SetQuickFixList(QuickFixItem[] quickFixItems);
  Task SetQuickFixListSilent(QuickFixItem[] quickFixItems);
  Task<bool> BuildProject(string projectPath, CancellationToken cancellationToken);
  Task<bool> ApplyWorkspaceEdit(WorkspaceEdit edit);

  Task<T?> RequestPickerAsync<T>(
    string prompt,
    PickerChoice<T>[] choices,
    Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
    CancellationToken ct = default);

  Task<T[]?> RequestMultiPickerAsync<T>(
    string prompt,
    PickerChoice<T>[] choices,
    Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
    CancellationToken ct = default);

  Task<T?> RequestLivePickerAsync<T>(
    string prompt,
    Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
    Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
    CancellationToken ct = default);

  Task<T[]?> RequestMultiLivePickerAsync<T>(
    string prompt,
    Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
    Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
    CancellationToken ct = default);
}