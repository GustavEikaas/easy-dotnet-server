using EasyDotnet.Domain.Models.NetcoreDbg;

namespace EasyDotnet.Application.Interfaces;

public interface IDebugOrchestrator
{
  Task<Debugger.DebugSession> StartServerDebugSessionAsync(
    string projectPath,
    string sessionId,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Task<Debugger.DebugSession> StartClientDebugSessionAsync(
    string projectPath,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Debugger.DebugSession? GetSessionService(string projectPath);

  Task StopDebugSessionAsync(string projectPath);

  bool HasActiveSession(string projectPath);
}