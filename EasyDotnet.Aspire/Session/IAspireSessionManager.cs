namespace EasyDotnet.Aspire.Session;

public interface IAspireSessionManager
{
  /// <summary>
  /// Adds a new Aspire session
  /// </summary>
  void AddSession(AspireSession session);

  /// <summary>
  /// Gets an existing session by project path
  /// </summary>
  AspireSession? GetSession(string projectPath);

  /// <summary>
  /// Gets a session by its DCP instance ID
  /// </summary>
  AspireSession? GetSessionByDcpId(string dcpId);

  /// <summary>
  /// Terminates a session and cleans up resources
  /// </summary>
  Task TerminateSessionAsync(
      string projectPath,
      CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets all active sessions
  /// </summary>
  IReadOnlyCollection<AspireSession> GetActiveSessions();

  /// <summary>
  /// Adds a run session to a parent Aspire session
  /// </summary>
  void AddRunSession(string dcpId, RunSession runSession);

  /// <summary>
  /// Gets a run session by its run ID
  /// </summary>
  RunSession? GetRunSession(string runId);

  /// <summary>
  /// Removes a run session
  /// </summary>
  bool RemoveRunSession(string runId);
}
