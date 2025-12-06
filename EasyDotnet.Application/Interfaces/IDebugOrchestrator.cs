using EasyDotnet.Debugger;
using EasyDotnet.Domain.Models.NetcoreDbg;

namespace EasyDotnet.Application.Interfaces;

public interface IDebugOrchestrator
{
  Task<int> StartServerDebugSessionAsync(
    string dllPath,
    string sessionId,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Task<int> StartClientDebugSessionAsync(
    string dllPath,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Debugger.DebugSession? GetSessionService(string dllPath);

  Task StopDebugSessionAsync(string dllPath);

  Domain.Models.NetcoreDbg.DebugSession? GetSession(string dllPath);

  bool HasActiveSession(string dllPath);
}