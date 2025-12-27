using EasyDotnet.Domain.Models.Client;

namespace EasyDotnet.Application.Interfaces;

public interface IClientService
{
  bool IsInitialized { get; set; }
  bool UseVisualStudio { get; set; }
  ProjectInfo? ProjectInfo { get; set; }
  ClientInfo? ClientInfo { get; set; }
  ClientOptions? ClientOptions { get; set; }

  void ThrowIfNotInitialized();
  Task<bool> RequestConfirmation(string prompt, bool defaultValue);
  Task<bool> RequestOpenBuffer(string path);
  Task<bool> RequestSetBreakpoint(string path, int lineNumber);
  Task<string?> RequestString(string prompt, string? defaultValue);
  Task<SelectionOption?> RequestSelection(string prompt, SelectionOption[] choices, string? defaultSelectionId = null);
  Task<SelectionOption[]?> RequestMultiSelection(string prompt, SelectionOption[] choices);
  Task<int> RequestStartDebugSession(string host, int port);
  Task<bool> RequestTerminateDebugSession(int sessionId);
  Task<Guid> RequestRunCommand(RunCommand command);
  Task SendProgressStart(string token, string title, string message, int? percentage = null);
  Task SendProgressUpdate(string token, string? message, int? percentage = null);
  Task SendProgressEnd(string token);
  Task SetQuickFixList(QuickFixItem[] quickFixItems);
  Task CloseQuickFixList();
}