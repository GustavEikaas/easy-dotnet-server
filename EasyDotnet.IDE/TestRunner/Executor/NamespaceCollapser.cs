using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Executor;

/// <summary>
/// Collapses single-child namespace chains (A → B → C with nothing else in A and B) into one
/// node displayed as "A.B.C". Operates on the discovered node list before it is registered and emitted.
/// </summary>
public static class NamespaceCollapser
{
  public static void Collapse(string projectNodeId, List<TestNode> nodes)
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
  }
}
