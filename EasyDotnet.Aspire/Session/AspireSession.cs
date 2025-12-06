using System.Diagnostics;

namespace EasyDotnet.Aspire.Session;

public class AspireSession
{
  public required string ProjectPath { get; init; }
  public required string DcpId { get; init; }
  public required Process AspireCliProcess { get; init; }
  public required DateTime StartedAt { get; init; }
  public required CancellationTokenSource SessionCts { get; init; }

  private readonly Dictionary<string, RunSession> _runSessions = [];
  private readonly object _lock = new();

  public IReadOnlyDictionary<string, RunSession> RunSessions
  {
    get
    {
      lock (_lock)
      {
        return new Dictionary<string, RunSession>(_runSessions);
      }
    }
  }

  public void AddRunSession(RunSession session)
  {
    lock (_lock)
    {
      _runSessions[session.RunId] = session;
    }
  }

  public bool RemoveRunSession(string runId)
  {
    lock (_lock)
    {
      return _runSessions.Remove(runId);
    }
  }

  public RunSession? GetRunSession(string runId)
  {
    lock (_lock)
    {
      return _runSessions.TryGetValue(runId, out var session) ? session : null;
    }
  }

  public bool IsRunning => !AspireCliProcess.HasExited;

  public void Dispose()
  {
    SessionCts?.Cancel();
    SessionCts?.Dispose();

    if (!AspireCliProcess.HasExited)
    {
      try
      {
        AspireCliProcess.Kill(entireProcessTree: true);
      }
      catch { }
    }

    AspireCliProcess?.Dispose();
  }
}