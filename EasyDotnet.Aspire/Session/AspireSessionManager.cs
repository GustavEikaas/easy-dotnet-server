using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Aspire.Session;

public class AspireSessionManager(ILogger<AspireSessionManager> logger) : IAspireSessionManager, IDisposable
{
  private readonly ConcurrentDictionary<string, AspireSession> _sessionsByProjectPath = new();
  private readonly ConcurrentDictionary<string, AspireSession> _sessionsByDcpId = new();
  private readonly ConcurrentDictionary<string, RunSession> _runSessionsById = new();

  public void AddSession(AspireSession session)
  {
    if (!_sessionsByProjectPath.TryAdd(session.ProjectPath, session))
    {
      throw new InvalidOperationException(
          $"Session already exists for project: {session.ProjectPath}");
    }

    if (!_sessionsByDcpId.TryAdd(session.DcpId, session))
    {
      _sessionsByProjectPath.TryRemove(session.ProjectPath, out _);
      throw new InvalidOperationException(
          $"Session already exists for DCP ID: {session.DcpId}");
    }

    logger.LogInformation(
        "Aspire session added: Project={ProjectPath}, DcpId={DcpId}",
        session.ProjectPath,
        session.DcpId);
  }

  public AspireSession? GetSession(string projectPath)
  {
    _sessionsByProjectPath.TryGetValue(projectPath, out var session);
    return session;
  }

  public AspireSession? GetSessionByDcpId(string dcpId)
  {
    _sessionsByDcpId.TryGetValue(dcpId, out var session);
    return session;
  }

  public async Task TerminateSessionAsync(
      string projectPath,
      CancellationToken cancellationToken = default)
  {
    if (!_sessionsByProjectPath.TryRemove(projectPath, out var session))
    {
      logger.LogWarning("No session found for project: {ProjectPath}", projectPath);
      return;
    }

    _sessionsByDcpId.TryRemove(session.DcpId, out _);

    // Remove all run sessions for this Aspire session
    foreach (var runSession in session.RunSessions.Values)
    {
      _runSessionsById.TryRemove(runSession.RunId, out _);
    }

    logger.LogInformation("Terminating Aspire session for project: {ProjectPath}", projectPath);

    session.Dispose();

    logger.LogInformation("Aspire session terminated: {ProjectPath}", projectPath);

    await Task.CompletedTask;
  }

  public IReadOnlyCollection<AspireSession> GetActiveSessions() => [.. _sessionsByProjectPath.Values];

  public void AddRunSession(string dcpId, RunSession runSession)
  {
    var session = GetSessionByDcpId(dcpId) ?? throw new InvalidOperationException($"No Aspire session found for DCP ID: {dcpId}");
    session.AddRunSession(runSession);
    _runSessionsById[runSession.RunId] = runSession;

    logger.LogInformation(
        "Run session added: RunId={RunId}, Project={ProjectPath}",
        runSession.RunId,
        runSession.ProjectPath);
  }

  public RunSession? GetRunSession(string runId)
  {
    _runSessionsById.TryGetValue(runId, out var session);
    return session;
  }

  public bool RemoveRunSession(string runId)
  {
    if (!_runSessionsById.TryRemove(runId, out var runSession))
    {
      return false;
    }

    // Also remove from parent session
    var parentSession = GetSessionByDcpId(runSession.DcpId);
    parentSession?.RemoveRunSession(runId);

    logger.LogInformation("Run session removed: {RunId}", runId);
    return true;
  }

  public void Dispose()
  {
    logger.LogInformation("Disposing AspireSessionManager, terminating all sessions");

    foreach (var session in _sessionsByProjectPath.Values)
    {
      try
      {
        session.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error disposing session for {ProjectPath}", session.ProjectPath);
      }
    }

    _sessionsByProjectPath.Clear();
    _sessionsByDcpId.Clear();
    _runSessionsById.Clear();
  }
}