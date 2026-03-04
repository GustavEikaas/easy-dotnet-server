using System.Collections.Concurrent;
using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Registry;

public class NodeRegistry
{
    private readonly ConcurrentDictionary<string, TestNode> _nodes = new();

    // Stable protocol ID → native framework ID (VSTest GUID or MTP Uid)
    // Only populated for TestMethod and Subcase nodes
    private readonly ConcurrentDictionary<string, string> _stableToNative = new();

    // Native framework ID → stable protocol ID (reverse lookup)
    private readonly ConcurrentDictionary<string, string> _nativeToStable = new();

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

    /// <summary>Returns all leaf nodes (TestMethod, Subcase) under a given root.</summary>
    public IEnumerable<TestNode> GetLeafDescendants(string rootId) =>
        GetDescendants(rootId).Where(n => n.Type is Models.NodeType.TestMethod or Models.NodeType.Subcase);

    public void Clear()
    {
        _nodes.Clear();
        _stableToNative.Clear();
        _nativeToStable.Clear();
    }

    public void ClearDescendants(string rootId)
    {
        foreach (var descendant in GetDescendants(rootId).ToList())
        {
            _nodes.TryRemove(descendant.Id, out _);
            if (_stableToNative.TryRemove(descendant.Id, out var native))
                _nativeToStable.TryRemove(native, out _);
        }
    }

    public bool Exists(string stableId) => _nodes.ContainsKey(stableId);
}
