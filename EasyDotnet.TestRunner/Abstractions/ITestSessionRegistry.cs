using EasyDotnet.TestRunner.Models;

namespace EasyDotnet.TestRunner.Abstractions;

public interface ITestSessionRegistry
{
  IEnumerable<TestNode> GetAllNodes();
  TestNode? GetNode(string id);
  IDisposable AcquireLock();
  void RegisterNode(TestNode node);
  void UpdateStatus(string nodeId, TestNodeStatus newStatus);
  bool Contains(string nodeId);
  bool TryGetNode(string nodeId, out TestNode? node);
  void UpdateNodeDisplayName(string nodeId, string newDisplayName);
  void UpdateNodeParent(string nodeId, string newParentId);
  void RemoveNode(string nodeId);
}