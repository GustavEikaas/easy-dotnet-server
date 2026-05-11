using System.Collections.Concurrent;
using EasyDotnet.IDE.Interfaces;

namespace EasyDotnet.IDE.Services;

public sealed class OpenBufferService : IOpenBufferService
{
  private static readonly StringComparer PathComparer =
      OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

  private readonly ConcurrentDictionary<string, byte> _open = new(PathComparer);

  public event Action<string>? BufferOpened;
  public event Action<string>? BufferClosed;

  public void OnBufferOpened(string path)
  {
    if (string.IsNullOrEmpty(path)) return;
    var normalized = Normalize(path);
    if (_open.TryAdd(normalized, 0))
      BufferOpened?.Invoke(normalized);
  }

  public void OnBufferClosed(string path)
  {
    if (string.IsNullOrEmpty(path)) return;
    var normalized = Normalize(path);
    if (_open.TryRemove(normalized, out _))
      BufferClosed?.Invoke(normalized);
  }

  public bool IsOpen(string path)
  {
    if (string.IsNullOrEmpty(path)) return false;
    return _open.ContainsKey(Normalize(path));
  }

  public IReadOnlySet<string> Snapshot() => _open.Keys.ToHashSet(PathComparer);

  private static string Normalize(string path)
  {
    try { return Path.GetFullPath(path); }
    catch { return path; }
  }
}
