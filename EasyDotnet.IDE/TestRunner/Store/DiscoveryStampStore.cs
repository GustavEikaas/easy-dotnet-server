using System.Collections.Concurrent;
using System.IO.Abstractions;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;

namespace EasyDotnet.IDE.TestRunner.Store;

/// <summary>
/// Remembers which test assembly each project node was last successfully discovered from,
/// so a run after a no-op build can skip re-discovery (and the registerTest flood it causes).
/// A project only counts as fresh while its assembly stamp is unchanged and the registry
/// still holds its discovered children.
/// </summary>
public class DiscoveryStampStore(NodeRegistry registry, IFileSystem fileSystem)
{
  private readonly ConcurrentDictionary<string, DiscoveredAssembly> _stamps = new(StringComparer.Ordinal);

  public AssemblyStamp? Capture(string targetPath) => AssemblyStamp.TryCapture(fileSystem, targetPath);

  /// <summary>
  /// Records a completed discovery. <paramref name="stamp"/> must be captured before discovery
  /// started, so a rebuild racing the discovery is picked up next time.
  /// </summary>
  public void MarkDiscovered(string projectNodeId, string targetPath, AssemblyStamp? stamp)
  {
    if (stamp is { } s)
      _stamps[projectNodeId] = new DiscoveredAssembly(targetPath, s);
    else
      _stamps.TryRemove(projectNodeId, out _);
  }

  public void Invalidate(string projectNodeId) => _stamps.TryRemove(projectNodeId, out _);

  public void Clear() => _stamps.Clear();

  /// <summary>
  /// True when the project must be re-discovered before running <paramref name="scopeNodeId"/>
  /// (defaults to the whole project): never discovered, discovery failed or was cancelled,
  /// the assembly changed, the registry lost its children, or the scope contains a
  /// ProbableTest — a test written after the last discovery that only discovery can resolve.
  /// </summary>
  public bool NeedsDiscovery(string projectNodeId, string targetPath, string? scopeNodeId = null) =>
      !_stamps.TryGetValue(projectNodeId, out var discovered)
      || !string.Equals(discovered.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase)
      || Capture(targetPath) != discovered.Stamp
      || !registry.HasDescendants(projectNodeId)
      || ContainsProbableTest(scopeNodeId ?? projectNodeId);

  private bool ContainsProbableTest(string scopeNodeId) =>
      registry.Get(scopeNodeId)?.Type is NodeType.ProbableTest
      || registry.GetDescendants(scopeNodeId).Any(n => n.Type is NodeType.ProbableTest);

  private sealed record DiscoveredAssembly(string TargetPath, AssemblyStamp Stamp);
}
