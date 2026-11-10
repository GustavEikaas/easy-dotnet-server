using System.Collections.Concurrent;
using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Registry;

public class NodeRegistry
{
  private readonly ConcurrentDictionary<string, TestNode> _nodes = new();
  private readonly ConcurrentDictionary<string, string> _stableToNative = new();
  private readonly ConcurrentDictionary<string, string> _nativeToStable = new();

  private readonly ConcurrentDictionary<string, TestNodeStatusKind?> _lastStatus = new();

  // Parent → child ids index so tree walks are O(subtree) instead of a full scan per visited node.
  // All structural writes (_nodes + _children + _leafCount) happen under _structureLock so the
  // index never disagrees with _nodes; point reads of _nodes stay lock-free.
  private readonly object _structureLock = new();
  private readonly Dictionary<string, HashSet<string>> _children = new(StringComparer.Ordinal);
  private int _leafCount;

  /// <summary>
  /// Upserts a node. If nativeId is provided, registers the bidirectional ID mapping.
  /// </summary>
  public void Register(TestNode node, string? nativeId = null)
  {
    lock (_structureLock)
    {
      if (_nodes.TryGetValue(node.Id, out var existing))
      {
        if (existing.ParentId != node.ParentId)
        {
          RemoveFromParentIndex(existing);
          AddToParentIndex(node);
        }

        _leafCount += (IsCountedLeaf(node) ? 1 : 0) - (IsCountedLeaf(existing) ? 1 : 0);
      }
      else
      {
        AddToParentIndex(node);
        if (IsCountedLeaf(node)) _leafCount++;
      }

      _nodes[node.Id] = node;
    }

    if (nativeId is not null)
    {
      _stableToNative[node.Id] = nativeId;
      _nativeToStable[nativeId] = node.Id;
    }
  }

  /// <summary>
  /// Records the last dispatched status for a node and returns whether it changed.
  /// The StatusDispatcher calls this before sending — if unchanged, skip the notification.
  /// </summary>
  public bool SetLastStatus(string stableId, TestNodeStatus? status)
  {
    var key = status?.Kind;
    var previous = _lastStatus.GetOrAdd(stableId, (TestNodeStatusKind?)null);

    if (previous == key)
      return false; // unchanged — suppress

    _lastStatus[stableId] = key;
    return true; // changed — send
  }

  public TestNodeStatusKind? GetLastStatusKind(string stableId) =>
      _lastStatus.TryGetValue(stableId, out var s) ? s : null;

  public TestNode? Get(string stableId) =>
      _nodes.TryGetValue(stableId, out var node) ? node : null;

  public string? GetNativeId(string stableId) =>
      _stableToNative.TryGetValue(stableId, out var id) ? id : null;

  public string? GetStableId(string nativeId) =>
      _nativeToStable.TryGetValue(nativeId, out var id) ? id : null;

  public IEnumerable<TestNode> GetAll() => _nodes.Values;

  /// <summary>Returns a snapshot of the direct children of a node.</summary>
  public IEnumerable<TestNode> GetChildren(string parentId)
  {
    lock (_structureLock)
    {
      return _children.TryGetValue(parentId, out var childIds)
          ? childIds.Select(id => _nodes[id]).ToList()
          : [];
    }
  }

  public bool HasDescendants(string rootId)
  {
    lock (_structureLock)
    {
      return _children.ContainsKey(rootId);
    }
  }

  /// <summary>Recursively returns all descendants of a node.</summary>
  public IEnumerable<TestNode> GetDescendants(string rootId)
  {
    var queue = new Queue<string>();
    queue.Enqueue(rootId);

    while (queue.Count > 0)
    {
      var current = queue.Dequeue();
      foreach (var child in GetChildren(current))
      {
        yield return child;
        queue.Enqueue(child.Id);
      }
    }
  }

  /// <summary>Returns all leaf nodes (TestMethod, Subcase, ProbableTest) under a given root.</summary>
  public IEnumerable<TestNode> GetLeafDescendants(string rootId) =>
      GetDescendants(rootId).Where(n => n.Type is NodeType.TestMethod or NodeType.Subcase or NodeType.ProbableTest);

  public void Clear()
  {
    lock (_structureLock)
    {
      _nodes.Clear();
      _children.Clear();
      _leafCount = 0;
    }
    _stableToNative.Clear();
    _nativeToStable.Clear();
    _lastStatus.Clear();
  }

  public void ClearDescendants(string rootId)
  {
    lock (_structureLock)
    {
      foreach (var id in CollectDescendantIds(rootId))
        RemoveNode(id);
    }
  }

  /// <summary>
  /// Removes a node and all its descendants from the registry.
  /// Returns the removed node IDs with descendants first and the root last.
  /// </summary>
  public List<string> RemoveSubtree(string rootId)
  {
    lock (_structureLock)
    {
      if (!Exists(rootId)) return [];

      var removedIds = CollectDescendantIds(rootId);
      removedIds.Add(rootId);

      foreach (var id in removedIds)
        RemoveNode(id);

      return removedIds;
    }
  }

  public int GetLeafCount() => Volatile.Read(ref _leafCount);

  public bool Exists(string stableId) => _nodes.ContainsKey(stableId);

  /// <summary>
  /// Returns all TestMethod/Subcase nodes whose FilePath matches the given path.
  /// Comparison is case-insensitive and normalises backslashes to forward slashes.
  /// </summary>
  public IEnumerable<TestNode> GetNodesForFile(string filePath)
  {
    var normalized = filePath.Replace('\\', '/');
    return _nodes.Values.Where(n =>
        (n.Type is NodeType.TestMethod or NodeType.Subcase or NodeType.TheoryGroup or NodeType.TestClass or NodeType.ProbableTest) &&
        string.Equals(
            n.FilePath?.Replace('\\', '/'),
            normalized,
            StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Replaces the position fields on a TestMethod/Subcase node in-place.
  /// Returns false if the node doesn't exist or positions are unchanged.
  /// </summary>
  public bool UpdateLineNumbers(
      string stableId, int signatureLine, int bodyStartLine, int endLine)
  {
    // Under the structure lock so a concurrent re-parenting Register can't be overwritten
    // with the stale ParentId captured here.
    lock (_structureLock)
    {
      if (!_nodes.TryGetValue(stableId, out var node)) return false;

      // No-op if nothing changed — avoids unnecessary re-renders
      if (node.SignatureLine == signatureLine &&
          node.BodyStartLine == bodyStartLine &&
          node.EndLine == endLine)
      {
        return false;
      }


      _nodes[stableId] = node with
      {
        SignatureLine = signatureLine,
        BodyStartLine = bodyStartLine,
        EndLine = endLine,
      };
      return true;
    }
  }

  // Must be called under _structureLock. Breadth-first, matching GetDescendants order.
  private List<string> CollectDescendantIds(string rootId)
  {
    var result = new List<string>();
    var queue = new Queue<string>();
    queue.Enqueue(rootId);

    while (queue.Count > 0)
    {
      if (!_children.TryGetValue(queue.Dequeue(), out var childIds)) continue;
      foreach (var childId in childIds)
      {
        result.Add(childId);
        queue.Enqueue(childId);
      }
    }

    return result;
  }

  // Must be called under _structureLock.
  private void RemoveNode(string id)
  {
    if (_nodes.TryRemove(id, out var removed))
    {
      RemoveFromParentIndex(removed);
      if (IsCountedLeaf(removed)) _leafCount--;
    }

    _lastStatus.TryRemove(id, out _);
    if (_stableToNative.TryRemove(id, out var native))
      _nativeToStable.TryRemove(native, out _);
  }

  // Must be called under _structureLock.
  private void AddToParentIndex(TestNode node)
  {
    if (node.ParentId is null) return;

    if (!_children.TryGetValue(node.ParentId, out var siblings))
    {
      siblings = new HashSet<string>(StringComparer.Ordinal);
      _children[node.ParentId] = siblings;
    }

    siblings.Add(node.Id);
  }

  // Must be called under _structureLock.
  private void RemoveFromParentIndex(TestNode node)
  {
    if (node.ParentId is null || !_children.TryGetValue(node.ParentId, out var siblings)) return;

    siblings.Remove(node.Id);
    if (siblings.Count == 0)
      _children.Remove(node.ParentId);
  }

  private static bool IsCountedLeaf(TestNode node) => node.Type is NodeType.TestMethod or NodeType.Subcase;
}
