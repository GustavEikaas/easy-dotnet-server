using System.Collections.Concurrent;

namespace EasyDotnet.IDE.Workspace.Services;

public record RunningProcessEntry(
    string SessionKey,
    string ProjectName,
    string ProjectFullPath,
    string? TargetFramework,
    int Pid
);

public record RunningSessionEntry(string ProjectName, bool IsDebugging);

/// <summary>
/// Combined session mutex and process data store, keyed by session key
/// (<c>"{projectPath}:{tfm}"</c> for run; <c>"watch:{path}"</c> for watch; <c>"debug:{projectPath}"</c> for debug).
///
/// <para>
/// A <c>null</c> value means the slot is claimed but no PID has been received yet
/// (watch sessions, or run sessions before the startup hook fires). A non-null value
/// means the session has a live process with known PID and full project metadata.
/// </para>
///
/// Lifecycle for a run session:
/// <list type="number">
///   <item><see cref="TryClaim"/> — claim the slot before starting the process.</item>
///   <item><see cref="SetProcessInfo"/> — fill in PID once the startup hook fires.</item>
///   <item><see cref="Release"/> — clean up when the process exits.</item>
/// </list>
///
/// Watch and debug sessions use only <see cref="TryClaim"/> and <see cref="Release"/>.
/// </summary>
public class WorkspaceSessionRegistry
{
  private readonly ConcurrentDictionary<string, RunningProcessEntry?> _sessions = new();
  private readonly ConcurrentDictionary<string, RunningSessionEntry> _claimedEntries = new();

  /// <summary>Claims the slot. Returns <c>false</c> if already active.</summary>
  public bool TryClaim(string key, string projectName, bool isDebug = false)
  {
    if (!_sessions.TryAdd(key, null))
      return false;
    _claimedEntries[key] = new RunningSessionEntry(projectName, isDebug);
    return true;
  }

  /// <summary>Returns <c>true</c> if a slot exists for the given key.</summary>
  public bool IsActive(string key) => _sessions.ContainsKey(key);

  /// <summary>Fills in the PID once the startup hook fires. No-op if the key is not claimed.</summary>
  public void SetProcessInfo(string key, RunningProcessEntry entry)
  {
    _sessions.TryUpdate(key, entry, null);
  }

  /// <summary>
  /// Updates the <c>IsDebugging</c> flag on an existing claimed session.
  /// No-op if the key is not present.
  /// </summary>
  public void SetDebugging(string key, bool isDebugging)
  {
    _claimedEntries.AddOrUpdate(
        key,
        _ => new RunningSessionEntry("", isDebugging),
        (_, existing) => existing with { IsDebugging = isDebugging });
  }

  /// <summary>Removes the session slot.</summary>
  public void Release(string key)
  {
    _sessions.TryRemove(key, out _);
    _claimedEntries.TryRemove(key, out _);
  }

  /// <summary>
  /// Returns metadata for all currently claimed sessions, including those that have not
  /// yet received a PID. Used for status notifications.
  /// </summary>
  public IReadOnlyList<RunningSessionEntry> GetAllRunningSessions() =>
    [.. _claimedEntries.Values];

  /// <summary>
  /// Returns all sessions that have received a PID — i.e. live run sessions eligible
  /// for debug-attach or process kill.
  /// </summary>
  public IReadOnlyList<RunningProcessEntry> GetRunningProcesses() =>
    [.. _sessions.Values.OfType<RunningProcessEntry>()];
}