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
  Task<SelectionOption<T>?> RequestSelection<T>(string prompt, SelectionOption<T>[] choices, string? defaultSelectionId = null);
  Task<SelectionOption<T>[]?> RequestMultiSelection<T>(string prompt, SelectionOption<T>[] choices);
  Task<int> RequestStartDebugSession(string host, int port);
  Task<bool> RequestTerminateDebugSession(int sessionId);
  Task SendProgress(string token, string kind, string? title = null, string? message = null, int? percentage = null);
}