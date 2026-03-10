using EasyDotnet.BuildServer.Contracts;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// Resolves and caches adapters per project TFM.
/// VsTestAdapters are kept alive (warm wrapper) until the project is invalidated.
/// MtpAdapters are stateless and created fresh per call.
/// </summary>
public class AdapterResolver(MtpAdapter mtpAdapter, VsTestAdapter vsTestAdapter)
{
  // Keyed by project node ID — one warm VsTestAdapter per project TFM
  private readonly Dictionary<string, VsTestAdapter> _vsTestAdapters = [];

  public ITestAdapter Resolve(ValidatedDotnetProject project)
  {
    if (project.IsMTP)
    {
      return mtpAdapter;
    }

    if (!_vsTestAdapters.TryGetValue(project.ProjectFullPath, out var adapter))
    {
      _vsTestAdapters[project.ProjectFullPath] = vsTestAdapter;
      return vsTestAdapter;
    }

    return adapter;
  }

  /// <summary>
  /// Called during invalidate — disposes the warm wrapper so the next
  /// Resolve() call creates a fresh one against the rebuilt DLL.
  /// </summary>
  public async Task InvalidateAsync(string projectPath)
  {
    if (_vsTestAdapters.Remove(projectPath, out var adapter))
      await adapter.DisposeAsync();
  }

  public async Task InvalidateAllAsync()
  {
    foreach (var adapter in _vsTestAdapters.Values)
      await adapter.DisposeAsync();
    _vsTestAdapters.Clear();
  }
}