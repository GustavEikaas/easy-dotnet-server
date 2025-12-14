using System.Collections.Concurrent;
using EasyDotnet.Domain.Models.NetcoreDbg;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Services;

public interface IDebugSessionManager
{
  Task<Debugger.DebugSession> StartServerSessionAsync(string projectPath, string sessionId,
    Func<Task<Debugger.DebugSession>> sessionFactory, CancellationToken cancellationToken);

  Task<Debugger.DebugSession> StartClientSessionAsync(string projectPath,
    Func<Task<Debugger.DebugSession>> sessionFactory, CancellationToken cancellationToken);

  Task EndSessionAsync(string projectPath, CancellationToken cancellationToken);

  DebugSession? GetSession(string projectPath);

  bool HasActiveSession(string projectPath);
}

public class DebugSessionManager(ILogger<DebugSessionManager> logger) : IDebugSessionManager
{
  private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
  private readonly ConcurrentDictionary<string, DebugSession> _activeSessions = new();
  private readonly ConcurrentDictionary<string, SemaphoreSlim> _dllLocks = new();

  public async Task<Debugger.DebugSession> StartServerSessionAsync(string projectPath, string sessionId,
    Func<Task<Debugger.DebugSession>> sessionFactory, CancellationToken cancellationToken) => await StartSessionInternalAsync(projectPath, sessionId, sessionFactory, cancellationToken);

  public async Task<Debugger.DebugSession> StartClientSessionAsync(string projectPath,
    Func<Task<Debugger.DebugSession>> sessionFactory, CancellationToken cancellationToken) => await StartSessionInternalAsync(projectPath, null, sessionFactory, cancellationToken);

  private async Task<Debugger.DebugSession> StartSessionInternalAsync(string projectPath, string? sessionId,
    Func<Task<Debugger.DebugSession>> sessionFactory, CancellationToken cancellationToken)
  {

    var projectName = Path.GetFileNameWithoutExtension(projectPath);
    var lockObj = _dllLocks.GetOrAdd(projectPath, _ => new SemaphoreSlim(1, 1));

    try
    {
      await lockObj.WaitAsync(LockTimeout, cancellationToken);
    }
    catch (OperationCanceledException)
    {
      logger.LogInformation("Lock acquisition cancelled for {projectPath}", projectName);
      throw;
    }

    try
    {
      if (_activeSessions.TryGetValue(projectPath, out var existing))
      {
        if (existing.State == DebugSessionState.Active ||
            existing.State == DebugSessionState.Starting)
        {
          throw new InvalidOperationException(
            $"A debug session is already in progress for {projectName}");
        }

        if (existing.State == DebugSessionState.Stopping)
        {
          logger.LogInformation("Waiting for previous session to finish cleaning up: {projectPath}", projectName);
          try
          {
            await existing.CleanupComplete.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
          }
          catch (TimeoutException)
          {
            logger.LogWarning("Cleanup wait timed out for {projectPath}, proceeding anyway", projectName);
          }
        }
      }

      var session = new DebugSession
      {
        DllPath = projectPath,
        SessionId = sessionId,
        State = DebugSessionState.Starting,
        StartedAt = DateTime.UtcNow
      };

      if (!_activeSessions.TryAdd(projectPath, session))
      {
        logger.LogWarning("Failed to register session for {projectName}", projectName);
        throw new InvalidOperationException($"Could not register debug session for {projectName}");
      }

      try
      {
        logger.LogInformation("Starting debug session for {projectName} (SessionId: {sessionId})",
          projectPath, sessionId ?? "client-initiated");

        var port = await sessionFactory();

        session.Port = port.Port;
        session.State = DebugSessionState.Active;

        logger.LogInformation("Debug session started for {projectName} on port {port}", projectName, port);
        return port;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Failed to start debug session for {projectName}", projectName);

        session.State = DebugSessionState.Stopping;
        _activeSessions.TryRemove(projectPath, out _);
        session.CleanupComplete.TrySetResult(true);

        throw;
      }
    }
    finally
    {
      lockObj.Release();
    }
  }

  public async Task EndSessionAsync(string projectPath, CancellationToken cancellationToken)
  {
    var projectName = Path.GetFileNameWithoutExtension(projectPath);
    if (!_activeSessions.TryGetValue(projectPath, out var session))
    {
      logger.LogWarning("Attempted to end non-existent session for {projectName}", projectName);
      return;
    }

    var lockObj = _dllLocks.GetOrAdd(projectPath, _ => new SemaphoreSlim(1, 1));

    try
    {
      await lockObj.WaitAsync(LockTimeout, cancellationToken);
    }
    catch (OperationCanceledException)
    {
      logger.LogInformation("Lock acquisition cancelled for {projectName}", projectName);
      return;
    }

    try
    {
      session.State = DebugSessionState.Stopping;
      logger.LogInformation("Ending debug session for {projectName} (SessionId: {sessionId})",
        projectName, session.SessionId ?? "client-initiated");

      _activeSessions.TryRemove(projectPath, out _);

      session.CleanupComplete.TrySetResult(true);

      session.State = DebugSessionState.Idle;
      logger.LogInformation("Debug session ended for {projectName}", projectName);
    }
    finally
    {
      lockObj.Release();
    }
  }

  public DebugSession? GetSession(string projectPath)
  {
    _activeSessions.TryGetValue(projectPath, out var session);
    return session;
  }

  public bool HasActiveSession(string projectPath)
  {
    var session = GetSession(projectPath);
    return session?.State == DebugSessionState.Active;
  }
}