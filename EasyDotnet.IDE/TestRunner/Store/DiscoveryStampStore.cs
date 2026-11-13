using System.Collections.Concurrent;
using System.IO.Abstractions;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;

namespace EasyDotnet.IDE.TestRunner.Store;

/// <summary>Remembers the assembly each project was last discovered from, so runs after a no-op build skip discovery.</summary>
public class DiscoveryStampStore(NodeRegistry registry, IFileSystem fileSystem)
{
  private readonly ConcurrentDictionary<string, DiscoveredAssembly> _stamps = new(StringComparer.Ordinal);

  public AssemblyStamp? Capture(string targetPath) => AssemblyStamp.TryCapture(fileSystem, targetPath);

  /// <summary><paramref name="stamp"/> must be captured before discovery started.</summary>
  public void MarkDiscovered(string projectNodeId, string targetPath, AssemblyStamp? stamp)
  {
    if (stamp is { } s)
      _stamps[projectNodeId] = new DiscoveredAssembly(targetPath, s);
    else
      _stamps.TryRemove(projectNodeId, out _);
  }

  public void Invalidate(string projectNodeId) => _stamps.TryRemove(projectNodeId, out _);

  public void Clear() => _stamps.Clear();

  public bool NeedsDiscovery(string projectNodeId, string targetPath, string? scopeNodeId = null) =>
      !_stamps.TryGetValue(projectNodeId, out var discovered)
      || !string.Equals(discovered.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase)
      || !registry.HasDescendants(projectNodeId)
      || ContainsProbableTest(scopeNodeId ?? projectNodeId)
      || Capture(targetPath) != discovered.Stamp;

  private bool ContainsProbableTest(string scopeNodeId) =>
      registry.Get(scopeNodeId)?.Type is NodeType.ProbableTest
      || registry.GetDescendants(scopeNodeId).Any(n => n.Type is NodeType.ProbableTest);

  private sealed record DiscoveredAssembly(string TargetPath, AssemblyStamp Stamp);
}