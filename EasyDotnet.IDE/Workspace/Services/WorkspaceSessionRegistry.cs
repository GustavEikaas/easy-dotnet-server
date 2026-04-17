using System.Collections.Concurrent;

namespace EasyDotnet.IDE.Workspace.Services;

public record RunningProcessEntry(
    string SessionKey,
    string ProjectName,
    string ProjectFullPath,
    string? TargetFramework,
    int Pid
);

/// <summary>
/// Combined session mutex and process data store, keyed by session key
/// (<c>"{projectPath}:{tfm}"</c> for run; <c>"watch:{path}"</c> for watch).
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
/// Watch sessions use only <see cref="TryClaim"/> and <see cref="Release"/>.
/// </summary>
public class WorkspaceSessionRegistry
{
  private readonly ConcurrentDictionary<string, RunningProcessEntry?> _sessions = new();

  /// <summary>Claims the slot. Returns <c>false</c> if already active.</summary>
  public bool TryClaim(string key) => _sessions.TryAdd(key, null);

  /// <summary>Returns <c>true</c> if a slot exists for the given key.</summary>
  public bool IsActive(string key) => _sessions.ContainsKey(key);

  /// <summary>Fills in the process info once a PID is known. No-op if the key is not claimed.</summary>
  public void SetProcessInfo(string key, RunningProcessEntry entry) =>
    _sessions.TryUpdate(key, entry, null);

  /// <summary>Removes the session slot.</summary>
  public void Release(string key) => _sessions.TryRemove(key, out _);

  /// <summary>
  /// Returns all sessions that have received a PID — i.e. live run sessions eligible
  /// for debug-attach.
  /// </summary>
  public IReadOnlyList<RunningProcessEntry> GetRunningProcesses() =>
    [.. _sessions.Values.OfType<RunningProcessEntry>()];
}
