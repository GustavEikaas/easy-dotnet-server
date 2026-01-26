using EasyDotnet.Domain.Models.Client;

namespace EasyDotnet.Application.Interfaces;

public interface IEditorService
{
  Task CloseQuickFixList();
  Task DisplayError(string message);
  Task DisplayMessage(string message);
  Task DisplayWarning(string message);
  Task<bool> RequestConfirmation(string prompt, bool defaultValue);
  Task<SelectionOption[]?> RequestMultiSelection(string prompt, SelectionOption[] choices);
  Task<bool> RequestOpenBuffer(string path, int? line);
  Task<Guid> RequestRunCommand(RunCommand command);
  Task<SelectionOption?> RequestSelection(string prompt, SelectionOption[] choices, string? defaultSelectionId = null);
  Task<bool> RequestSetBreakpoint(string path, int lineNumber);
  Task<int> RequestStartDebugSession(string host, int port);
  Task<string?> RequestString(string prompt, string? defaultValue);
  Task<bool> RequestTerminateDebugSession(int sessionId);
  Task SendProgressEnd(string token);
  Task SendProgressStart(string token, string title, string message, int? percentage = null);
  Task SendProgressUpdate(string token, string? message, int? percentage = null);
  Task SetQuickFixList(QuickFixItem[] quickFixItems);
}