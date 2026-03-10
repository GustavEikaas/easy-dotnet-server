using EasyDotnet.BuildServer.Contracts;

namespace EasyDotnet.IDE.TestRunner.Store;

/// <summary>
/// Stores build diagnostics per project node ID.
/// Populated when a build finishes with errors; cleared on invalidate or successful rebuild.
/// </summary>
public sealed class BuildErrorStore
{
  private readonly Dictionary<string, BuildDiagnostic[]> _errors = new(StringComparer.Ordinal);

  public void Set(string projectNodeId, BuildDiagnostic[] diagnostics)
  {
    lock (_errors) { _errors[projectNodeId] = diagnostics; }
  }

  public void Clear(string projectNodeId)
  {
    lock (_errors) { _errors.Remove(projectNodeId); }
  }

  public void ClearAll()
  {
    lock (_errors) { _errors.Clear(); }
  }

  public BuildDiagnostic[]? Get(string projectNodeId)
  {
    lock (_errors) { return _errors.GetValueOrDefault(projectNodeId); }
  }
}