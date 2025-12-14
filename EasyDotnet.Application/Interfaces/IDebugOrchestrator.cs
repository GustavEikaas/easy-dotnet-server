using EasyDotnet.Domain.Models.NetcoreDbg;

namespace EasyDotnet.Application.Interfaces;

public interface IDebugOrchestrator
{
  Task<Debugger.DebugSession> StartServerDebugSessionAsync(
    string dllPath,
    string sessionId,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Task<Debugger.DebugSession> StartClientDebugSessionAsync(
    string dllPath,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Debugger.DebugSession? GetSessionService(string dllPath);

  Task StopDebugSessionAsync(string dllPath);

  DebugSession? GetSession(string dllPath);

  bool HasActiveSession(string dllPath);
}