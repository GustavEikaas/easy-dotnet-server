using EasyDotnet.BuildServer.Contracts;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// Resolves and caches adapters per project TFM.
/// VsTestAdapters are kept alive (warm wrapper) until the project is invalidated.
/// MtpAdapters are stateless and created fresh per call.
/// </summary>
public class AdapterResolver(MtpAdapter mtpAdapter, Func<VsTestAdapter> vsTestAdapterFactory)
{
  // Keyed by project path + TFM — one warm VsTestAdapter per project variant.
  private readonly Dictionary<string, VsTestAdapter> _vsTestAdapters = [];

  public ITestAdapter Resolve(ValidatedDotnetProject project)
  {
    if (project.IsMTP)
    {
      return mtpAdapter;
    }

    var key = BuildKey(project.ProjectFullPath, project.TargetFramework);
    if (!_vsTestAdapters.TryGetValue(key, out var adapter))
    {
      adapter = vsTestAdapterFactory();
      _vsTestAdapters[key] = adapter;
    }

    return adapter;
  }

  /// <summary>
  /// Called during invalidate — disposes the warm wrapper so the next
  /// Resolve() call creates a fresh one against the rebuilt DLL.
  /// </summary>
  public async Task InvalidateAsync(string projectPath)
  {
    var prefix = BuildPrefix(projectPath);
    var keys = _vsTestAdapters.Keys
        .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var key in keys)
    {
      if (_vsTestAdapters.Remove(key, out var adapter))
      {
        await adapter.DisposeAsync();
      }
    }
  }

  public async Task InvalidateAllAsync()
  {
    foreach (var adapter in _vsTestAdapters.Values)
      await adapter.DisposeAsync();
    _vsTestAdapters.Clear();
  }

  private static string BuildKey(string projectPath, string targetFramework) =>
      $"{projectPath}::{targetFramework}";

  private static string BuildPrefix(string projectPath) =>
      $"{projectPath}::";
}