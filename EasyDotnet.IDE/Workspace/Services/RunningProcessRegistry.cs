using System.Collections.Concurrent;

namespace EasyDotnet.IDE.Workspace.Services;

public record RunningProcessEntry(
    string SessionKey,
    string ProjectName,
    string ProjectFullPath,
    string? TargetFramework,
    int Pid
);

public class RunningProcessRegistry
{
  private readonly ConcurrentDictionary<string, RunningProcessEntry> _entries = new();

  public void Register(RunningProcessEntry entry) =>
      _entries[entry.SessionKey] = entry;

  public void Unregister(string sessionKey) =>
      _entries.TryRemove(sessionKey, out _);

  public IReadOnlyList<RunningProcessEntry> GetAll() =>
      [.. _entries.Values];
}