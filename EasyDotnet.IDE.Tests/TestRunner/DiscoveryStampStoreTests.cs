using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using EasyDotnet.IDE.TestRunner.Store;
using static EasyDotnet.IDE.Tests.TestRunner.NodeRegistryTests;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class DiscoveryStampStoreTests
{
  private const string TargetPath = "/repo/tests/bin/Debug/net8.0/Tests.dll";
  private const string ProjectId = "proj";

  private readonly MockFileSystem _fileSystem = new();
  private readonly NodeRegistry _registry = new();
  private readonly DiscoveryStampStore _store;

  public DiscoveryStampStoreTests()
  {
    _fileSystem.AddFile(TargetPath, new MockFileData([1, 2, 3]));
    _fileSystem.File.SetLastWriteTimeUtc(TargetPath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    _registry.Register(Node("sln", null, new NodeType.Solution()));
    _registry.Register(Node(ProjectId, "sln", new NodeType.Project()));
    _registry.Register(Node("class", ProjectId, new NodeType.TestClass()));
    _registry.Register(Node("m1", "class", new NodeType.TestMethod()));

    _store = new DiscoveryStampStore(_registry, _fileSystem);
  }

  private void MarkFresh() => _store.MarkDiscovered(ProjectId, TargetPath, _store.Capture(TargetPath));

  [Test]
  public async Task NeverDiscovered_NeedsDiscovery() =>
      await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();

  [Test]
  public async Task UnchangedAssembly_SkipsDiscovery()
  {
    MarkFresh();

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsFalse();
    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath, scopeNodeId: "m1")).IsFalse();
  }

  [Test]
  public async Task RebuiltAssembly_NewWriteTime_NeedsDiscovery()
  {
    MarkFresh();

    _fileSystem.File.SetLastWriteTimeUtc(TargetPath, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();
  }

  [Test]
  public async Task RebuiltAssembly_NewSize_NeedsDiscovery()
  {
    MarkFresh();
    var lastWrite = _fileSystem.File.GetLastWriteTimeUtc(TargetPath);

    _fileSystem.File.WriteAllBytes(TargetPath, [1, 2, 3, 4]);
    _fileSystem.File.SetLastWriteTimeUtc(TargetPath, lastWrite);

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();
  }

  [Test]
  public async Task MissingAssembly_NeedsDiscovery()
  {
    MarkFresh();

    _fileSystem.File.Delete(TargetPath);

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();
  }

  [Test]
  public async Task DifferentTargetPath_NeedsDiscovery()
  {
    MarkFresh();
    const string otherPath = "/repo/tests/bin/Debug/net9.0/Tests.dll";
    _fileSystem.AddFile(otherPath, new MockFileData([1, 2, 3]));

    await Assert.That(_store.NeedsDiscovery(ProjectId, otherPath)).IsTrue();
  }

  [Test]
  public async Task Invalidated_NeedsDiscovery()
  {
    MarkFresh();

    _store.Invalidate(ProjectId);

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();
  }

  [Test]
  public async Task Cleared_NeedsDiscovery()
  {
    MarkFresh();

    _store.Clear();

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();
  }

  [Test]
  public async Task MarkedWithoutStamp_NeedsDiscovery()
  {
    MarkFresh();

    _store.MarkDiscovered(ProjectId, TargetPath, stamp: null);

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();
  }

  [Test]
  public async Task RegistryLostChildren_NeedsDiscovery()
  {
    MarkFresh();

    _registry.ClearDescendants(ProjectId);

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();
  }

  [Test]
  public async Task ProbableTestRequested_NeedsDiscovery()
  {
    MarkFresh();
    _registry.Register(Node("probable", "class", new NodeType.ProbableTest()));

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath, scopeNodeId: "probable")).IsTrue();
  }

  [Test]
  public async Task ProbableTestInsideScope_NeedsDiscovery()
  {
    MarkFresh();
    _registry.Register(Node("probable", "class", new NodeType.ProbableTest()));

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath, scopeNodeId: "class")).IsTrue();
    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath)).IsTrue();
  }

  [Test]
  public async Task ProbableTestOutsideScope_SkipsDiscovery()
  {
    MarkFresh();
    _registry.Register(Node("class2", ProjectId, new NodeType.TestClass()));
    _registry.Register(Node("probable", "class2", new NodeType.ProbableTest()));

    await Assert.That(_store.NeedsDiscovery(ProjectId, TargetPath, scopeNodeId: "m1")).IsFalse();
  }
}
