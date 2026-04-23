using System.Collections.Concurrent;
using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Registry;

public class NodeRegistry
{
  private readonly ConcurrentDictionary<string, TestNode> _nodes = new();
  private readonly ConcurrentDictionary<string, string> _stableToNative = new();
  private readonly ConcurrentDictionary<string, string> _nativeToStable = new();

  private readonly ConcurrentDictionary<string, TestNodeStatusKind?> _lastStatus = new();

  /// <summary>
  /// Upserts a node. If nativeId is provided, registers the bidirectional ID mapping.
  /// </summary>
  public void Register(TestNode node, string? nativeId = null)
  {
    _nodes[node.Id] = node;

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

  public IEnumerable<TestNode> GetChildren(string parentId) =>
      _nodes.Values.Where(n => n.ParentId == parentId);

  /// <summary>Recursively returns all descendants of a node.</summary>
  public bool HasDescendants(string rootId) => GetDescendants(rootId).Any();

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
    _nodes.Clear();
    _stableToNative.Clear();
    _nativeToStable.Clear();
    _lastStatus.Clear();
  }

  public void ClearDescendants(string rootId)
  {
    foreach (var descendant in GetDescendants(rootId).ToList())
    {
      _nodes.TryRemove(descendant.Id, out _);
      _lastStatus.TryRemove(descendant.Id, out _);
      if (_stableToNative.TryRemove(descendant.Id, out var native))
        _nativeToStable.TryRemove(native, out _);
    }
  }

  /// <summary>
  /// Removes a node and all its descendants from the registry.
  /// Returns the removed node IDs with descendants first and the root last.
  /// </summary>
  public List<string> RemoveSubtree(string rootId)
  {
    if (!Exists(rootId)) return [];

    var removedIds = GetDescendants(rootId)
        .Select(n => n.Id)
        .ToList();

    removedIds.Add(rootId);

    foreach (var id in removedIds)
    {
      _nodes.TryRemove(id, out _);
      _lastStatus.TryRemove(id, out _);
      if (_stableToNative.TryRemove(id, out var native))
        _nativeToStable.TryRemove(native, out _);
    }

    return removedIds;
  }

  public int GetLeafCount() =>
      _nodes.Values.Count(n => n.Type is NodeType.TestMethod or NodeType.Subcase);

  public bool Exists(string stableId) => _nodes.ContainsKey(stableId);

  /// <summary>
  /// Returns all TestMethod/Subcase nodes whose FilePath matches the given path.
  /// Comparison is case-insensitive and normalises backslashes to forward slashes.
  /// </summary>
  public IEnumerable<TestNode> GetNodesForFile(string filePath)
  {
    var normalized = filePath.Replace('\\', '/');
    return _nodes.Values.Where(n =>
        (n.Type is NodeType.TestMethod or NodeType.Subcase or NodeType.TheoryGroup or NodeType.TestClass) &&
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