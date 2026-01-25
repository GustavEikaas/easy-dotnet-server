using EasyDotnet.Domain.Models.Test;
using EasyDotnet.TestRunner.Abstractions;
using EasyDotnet.TestRunner.Models;

namespace EasyDotnet.TestRunner.Services;

public interface ITestHierarchyService
{
  void ProcessTestDiscovery(string projectNodeId, IEnumerable<DiscoveredTest> tests, ITestSessionRegistry registry);
}

public class TestHierarchyService : ITestHierarchyService
{
  public void ProcessTestDiscovery(string projectNodeId, IEnumerable<DiscoveredTest> tests, ITestSessionRegistry registry)
  {
    foreach (var group in tests.GroupBy(t => t.FullyQualifiedName))
    {
      ProcessMethodGroup(projectNodeId, group, registry);
    }

    CollapseSingleChildNamespaces(projectNodeId, registry);
  }

  private void ProcessMethodGroup(string rootId, IGrouping<string, DiscoveredTest> group, ITestSessionRegistry registry)
  {
    var firstTest = group.First();
    var parentId = rootId;
    var currentNamespace = "";

    foreach (var part in firstTest.NamespacePath)
    {
      currentNamespace = string.IsNullOrEmpty(currentNamespace) ? part : $"{currentNamespace}.{part}";
      var nsNodeId = $"ns:{currentNamespace}";

      if (!registry.Contains(nsNodeId))
      {
        registry.RegisterNode(new TestNode(
            Id: nsNodeId,
            DisplayName: part,
            ParentId: parentId,
            FilePath: null,
            LineNumber: null,
            Type: new NodeType.Namespace()
        ));
      }
      parentId = nsNodeId;
    }

    var isGroup = group.Count() > 1 || firstTest.Arguments != null;

    if (isGroup)
    {

      var methodName = firstTest.FullyQualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries).Last();
      var groupUniqueId = $"{parentId}/{methodName}";

      if (!registry.Contains(groupUniqueId))
      {
        registry.RegisterNode(new TestNode(
            Id: groupUniqueId,
            DisplayName: methodName, // The Code Name
            ParentId: parentId,
            FilePath: firstTest.FilePath,
            LineNumber: null,
            Type: new NodeType.TestGroup()
        ));
      }

      foreach (var test in group)
      {
        var leafLabel = test.Arguments ?? test.DisplayName;

        registry.RegisterNode(new TestNode(
            Id: test.Id,
            DisplayName: leafLabel,
            ParentId: groupUniqueId,
            FilePath: test.FilePath,
            LineNumber: test.LineNumber,
            Type: new NodeType.Subcase()
        ));
      }
    }
    else
    {

      registry.RegisterNode(new TestNode(
          Id: firstTest.Id,
          DisplayName: firstTest.DisplayName,
          ParentId: parentId,
          FilePath: firstTest.FilePath,
          LineNumber: firstTest.LineNumber,
          Type: new NodeType.TestMethod()
      ));
    }
  }

  private static void CollapseSingleChildNamespaces(string rootId, ITestSessionRegistry registry)
  {
    var allNodes = registry.GetAllNodes();

    var childrenByParent = allNodes
        .Where(n => n.ParentId != null)
        .GroupBy(n => n.ParentId!)
        .ToDictionary(g => g.Key, g => g.ToList());

    void Collapse(string parentId)
    {
      if (!childrenByParent.TryGetValue(parentId, out var children)) return;

      foreach (var child in children)
      {
        Collapse(child.Id);
      }

      foreach (var ns in children.Where(c => c.Type is NodeType.Namespace))
      {
        var current = ns;
        var mergedNameParts = new List<string> { current.DisplayName };
        var nodesToRemove = new List<string>();

        while (childrenByParent.TryGetValue(current.Id, out var singleChildList)
               && singleChildList.Count == 1
               && singleChildList[0].Type is NodeType.Namespace)
        {
          var child = singleChildList[0];
          mergedNameParts.Add(child.DisplayName);
          nodesToRemove.Add(child.Id);
          current = child;
        }

        if (mergedNameParts.Count > 1)
        {
          if (registry.TryGetNode(ns.Id, out _))
          {
            var newName = string.Join('.', mergedNameParts);
            registry.UpdateNodeDisplayName(ns.Id, newName);
          }

          if (childrenByParent.TryGetValue(current.Id, out var grandChildren))
          {
            foreach (var gc in grandChildren)
            {
              registry.UpdateNodeParent(gc.Id, ns.Id);
            }
          }

          foreach (var idToRemove in nodesToRemove)
          {
            registry.RemoveNode(idToRemove);
          }
        }
      }
    }

    Collapse(rootId);
  }
}