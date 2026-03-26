using System.Collections.Concurrent;

namespace EasyDotnet.IDE.Workspace.Services;

/// <summary>
/// Tracks active run (and future debug) sessions per project path.
/// </summary>
public class WorkspaceSessionManager
{
  private readonly ConcurrentDictionary<string, byte> _activeSessions = new();

  public bool TryRegister(string key) => _activeSessions.TryAdd(key, 0);

  public void Unregister(string key) => _activeSessions.TryRemove(key, out _);

  public bool IsActive(string key) => _activeSessions.ContainsKey(key);
}