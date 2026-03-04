using System.Collections.Concurrent;
using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Store;

/// <summary>
/// Caches per-node test result details (stdout, stack frames, error messages).
/// Written by OperationExecutor after each test completes.
/// Never pushed to the client — fetched lazily via testrunner/getResults.
/// Cleared at the start of any new operation on a node.
/// </summary>
public class DetailStore
{
    private readonly ConcurrentDictionary<string, TestDetail> _details = new();

    public void Set(string stableNodeId, TestDetail detail) =>
        _details[stableNodeId] = detail;

    public TestDetail? Get(string stableNodeId) =>
        _details.TryGetValue(stableNodeId, out var detail) ? detail : null;

    public void Clear(string stableNodeId) =>
        _details.TryRemove(stableNodeId, out _);

    /// <summary>Clears details for a node and all its descendants.</summary>
    public void ClearSubtree(IEnumerable<string> stableNodeIds)
    {
        foreach (var id in stableNodeIds)
            _details.TryRemove(id, out _);
    }

    public void ClearAll() => _details.Clear();

    public bool HasResults(string stableNodeId) =>
        _details.TryGetValue(stableNodeId, out var d) && d.HasResults;
}
