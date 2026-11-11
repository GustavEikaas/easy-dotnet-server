using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;

namespace EasyDotnet.IDE.TestRunner.Executor;

/// <summary>
/// Collapses single-child namespace chains (A → B → C with nothing else in A and B) into one
/// node displayed as "A.B.C", both in the discovered node list sent to the client and in the registry.
/// </summary>
public static class NamespaceCollapser
{
  public static void Collapse(string projectNodeId, List<TestNode> nodes, NodeRegistry registry)
  {
    var byId = nodes.ToDictionary(n => n.Id);
    var childrenOf = nodes
        .Where(n => n.ParentId is not null)
        .GroupBy(n => n.ParentId!)
        .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());

    var toRemove = new HashSet<string>();
    var toUpdate = new Dictionary<string, TestNode>();

    void Walk(string parentId)
    {
      foreach (var childId in childrenOf.GetValueOrDefault(parentId) ?? [])
      {
        if (!byId.TryGetValue(childId, out var child)) continue;

        if (child.Type is not NodeType.Namespace)
        {
          Walk(childId);
          continue;
        }

        var chain = new List<TestNode> { child };
        var current = child;
        while (true)
        {
          var grandchildIds = childrenOf.GetValueOrDefault(current.Id) ?? [];
          if (grandchildIds.Count == 1
              && byId.TryGetValue(grandchildIds[0], out var grandchild)
              && grandchild.Type is NodeType.Namespace)
          {
            chain.Add(grandchild);
            current = grandchild;
          }
          else
          {
            break;
          }

        }

        if (chain.Count > 1)
        {
          for (var i = 0; i < chain.Count - 1; i++)
            toRemove.Add(chain[i].Id);

          var terminal = chain[^1];
          toUpdate[terminal.Id] = terminal with
          {
            DisplayName = string.Join(".", chain.Select(n => n.DisplayName)),
            ParentId = chain[0].ParentId
          };
        }

        Walk(chain[^1].Id);
      }
    }

    Walk(projectNodeId);

    for (var i = nodes.Count - 1; i >= 0; i--)
    {
      var node = nodes[i];
      if (toRemove.Contains(node.Id))
        nodes.RemoveAt(i);
      else if (toUpdate.TryGetValue(node.Id, out var updated))
        nodes[i] = updated;
    }

    // Mirror the collapse in the registry so it holds the same tree the client receives.
    // Re-parent the terminals first: each collapsed chain node then has no descendants left
    // besides the rest of its chain, so RemoveSubtree drops exactly the collapsed nodes.
    foreach (var updated in toUpdate.Values)
      registry.Register(updated);

    foreach (var id in toRemove)
      registry.RemoveSubtree(id);
  }
}
