using System.Collections.Concurrent;
using System.Data;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.TestRunner.Models;
using EasyDotnet.TestRunner.Notifications;
using EasyDotnet.TestRunner.Requests;
using StreamJsonRpc;

namespace EasyDotnet.TestRunner.Services;


public class TestRunnerService(JsonRpc jsonRpc, IMsBuildService msBuildService, ISolutionService solutionService, IVsTestService vsTestService) : ITestRunner
{
  private TestNode? _solutionNode;
  private readonly List<ProjectTfm> _projects = [];
  private readonly Dictionary<string, NodeRegistry> _projectTfmRegistries = [];
  private bool _isInitialized;


  public async Task InitializeAsync(string solutionFilePath, CancellationToken cancellationToken)
  {
    if (_isInitialized) return;

    _solutionNode = CreateSolutionNode(solutionFilePath);
    OnRegisterTest(_solutionNode);
    OnUpdateStatus(new TestNodeStatusUpdateNotification(_solutionNode.Id, new TestNodeStatus.Discovering()));

    var projects = solutionService.GetProjectsFromSolutionFile(solutionFilePath);
    var dotnetProjects = await Task.WhenAll(projects.Select(x => x.AbsolutePath).Select(x => msBuildService.GetOrSetProjectPropertiesAsync(x, cancellationToken: cancellationToken)));
    OnUpdateStatus(new TestNodeStatusUpdateNotification(_solutionNode.Id, new TestNodeStatus.Idle()));
    var testProjects = dotnetProjects.Where(x => x.IsTestProject || x.IsTestingPlatformApplication || x.TestingPlatformDotnetTestSupport);

    var flattened = testProjects
        .SelectMany(project =>
        {
          var projectFilePath = project.MSBuildProjectFullPath ?? string.Empty;
          var projectName = project.MSBuildProjectName ?? "UnknownProject";

          if (project.TargetFrameworks?.Length > 0)
          {
            return project.TargetFrameworks
            .Where(tfm => tfm != null)
            .Select(tfm => new ProjectTfm(
                Id: Guid.NewGuid().ToString(),
                ProjectFilePath: projectFilePath,
                DisplayName: $"{projectName} ({tfm})",
                TargetFramework: tfm
            ));
          }

          var tfmFallback = project.TargetFramework ?? "netunknown";
          return [new ProjectTfm(
            Id: Guid.NewGuid().ToString(),
            ProjectFilePath: projectFilePath,
            DisplayName: $"{projectName} ({tfmFallback})",
            TargetFramework: tfmFallback
        )];
        })
        .ToList();


    foreach (var tfm in flattened)
    {
      _projectTfmRegistries[tfm.Id] = new NodeRegistry();

      var node = tfm.ToTestNode(_solutionNode.Id);
      _projects.Add(tfm);
      OnRegisterTest(node);
    }

    _isInitialized = true;
  }


  public Task StartDiscoveryAsync(CancellationToken cancellationToken)
  {
    if (!_isInitialized) throw new InvalidOperationException("Testrunner has not been initialized");

    //TODO: rebuild
    //msbuild _projects.select(x => x.ProjectFilePath)

    _projects.ToList().ForEach(async (val) =>
    {
      var project = await msBuildService.GetOrSetProjectPropertiesAsync(val.ProjectFilePath, val.TargetFramework, cancellationToken: cancellationToken);
      var registry = _projectTfmRegistries[val.Id] ?? throw new InvalidOperationException($"No node registry found for {val.DisplayName}");
      await OperationScopeAsync(registry, (tracker, ct) =>
      {
        tracker.UpdateStatus(val.ToTestNode(_solutionNode!.Id), new TestNodeStatus.Discovering());



        if (project.TestingPlatformDotnetTestSupport)
        {
          // MTP
        }
        else
        {

          // TODO:
          // if test.DisplayName.StartsWith(project.RootNamespace) strip it away
          // collapse namespaces with only one child
          // the last .xxx of the ns is the actual test name so we should drop that

          foreach (var test in vsTestService.RunDiscover(project.TargetPath!))
          {
            var ns = test.Name ?? test.Namespace ?? "";
            var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0) continue;

            var methodName = parts[^1];
            var namespaceParts = parts[..^1];

            // Start at project level
            var parentId = val.Id;

            // Build namespace hierarchy
            var currentNamespace = "";
            foreach (var part in namespaceParts)
            {
              currentNamespace = string.IsNullOrEmpty(currentNamespace)
                  ? part
                  : $"{currentNamespace}.{part}";

              var nsNodeId = $"ns:{currentNamespace}";

              if (!registry.TryGetNode(nsNodeId, out _))
              {
                var nsNode = new TestNode(
                    nsNodeId,
                    part,          // displayName
                    parentId,      // parentId
                    null,          // file path
                    null,          // line
                    new NodeType.Namespace()
                );
                tracker.RegisterNode(nsNode);
              }

              parentId = nsNodeId;
            }


            if (test.DisplayName.Contains('('))
            {

              var testGroupId = test.DisplayName.Split('(')[0];


              if (!registry.TryGetNode(testGroupId, out _))
              {
                var testGroup = new TestNode(
                  testGroupId,
                  GetMethodName(test.DisplayName),
                  parentId,
                  test.FilePath,
                  LineNumber: null,
                  new NodeType.TestGroup()
                );

                tracker.RegisterNode(testGroup);
              }

              //TODO: need to make a parent with a deterministic id that all the sibling cases would also produce
              var subCase = new TestNode(
                  test.Id,
                  GetArguments(test.DisplayName),
                  testGroupId,
                  test.FilePath,
                  test.LineNumber,
                  new NodeType.Subcase()
              );

              tracker.RegisterNode(subCase);
            }
            else
            {
              var testNode = new TestNode(
                  test.Id,
                  GetMethodName(test.DisplayName),
                  parentId,
                  test.FilePath,
                  test.LineNumber,
                  new NodeType.TestMethod()
              );

              tracker.RegisterNode(testNode);
            }
          }

          // tests.ForEach(x =>
          // {
          //   var y = new TestNode(x.Id, x.DisplayName, val.Id, x.FilePath, x.LineNumber, new NodeType.TestMethod());
          //   tracker.RegisterNode(y);
          // });
          // Vstest
        }
        //We push tests as they are discovered to the client, once the entire discovery is finished we can add aggregated data like "total tests"
        // this can live under an optional field in testnode similiar to code_lens stuff totalTests, failedLastOp, passedLastOp, skippedLastOp, conclusion


        return Task.CompletedTask;
      }, cancellationToken);
    });


    //immediately register a node that represents the solution testnode
    //start resolving the projects in the sln
    //one by one register the projects as testnodes
    //continuosly while doing this "transaction" store a reference internally of all nodes
    //if at any point in this scope the cancellation token is invoked we should push a "cancellation" status update
    //when the request ends due to cancelling we push a "cancelled" status update for all nodes affected in this scope
    ////if no cancellation is requested 
    ////start by building all projects in the solution file, push an update saying all the projects are "building"
    ///after build finishes either push a build failed/build cancelled or build succeeded (which would immediately trigger next state)
    ///push a discovery status update
    ///// the discovery service supports yielding and will give us one by one test (at this point we can start pushing tests to the client)
    /////once the discovery is finished we can clear the status of the project
    return Task.CompletedTask;
  }

  public Task RunTestsAsync(RunRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
  public Task DebugTestsAsync(DebugRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();

  /// <summary>
  /// Encapsulates a "scoped operation" on test nodes.
  /// Tracks nodes registered during the scope and handles cancellation automatically.
  /// </summary>
  private async Task OperationScopeAsync(NodeRegistry registry, Func<NodeScopeTracker, CancellationToken, Task> operation, CancellationToken ct)
  {
    var tracker = registry.BeginScope(OnRegisterTest, OnUpdateStatus);

    try
    {
      await operation(tracker, ct);
    }
    catch (OperationCanceledException)
    {
      foreach (var node in tracker.RegisteredNodes)
        tracker.UpdateStatus(node, new TestNodeStatus.Cancelling());

      foreach (var node in tracker.RegisteredNodes)
        tracker.UpdateStatus(node, new TestNodeStatus.Cancelled());

      throw;
    }
  }

  private static string GetMethodName(string raw)
  {
    if (string.IsNullOrEmpty(raw))
      return raw;

    var lastDot = raw.LastIndexOf('.');
    var methodPart = lastDot >= 0 ? raw[(lastDot + 1)..] : raw;

    var parenIdx = methodPart.IndexOf('(');
    if (parenIdx > 0)
      methodPart = methodPart[..parenIdx];

    return methodPart;
  }

  private static string GetArguments(string raw)
  {
    var start = raw.IndexOf('(');
    var end = raw.LastIndexOf(')');
    if (start >= 0 && end > start)
      return raw[start..(end + 1)];

    return string.Empty;
  }

  private void OnRegisterTest(TestNode testNode)
  {
    var _ = jsonRpc.NotifyWithParameterObjectAsync("registerTest", testNode);
  }

  private void OnUpdateStatus(TestNodeStatusUpdateNotification notification)
  {
    var _ = jsonRpc.NotifyWithParameterObjectAsync("updateStatus", notification);
  }

  private static TestNode CreateSolutionNode(string solutionFilePath) => new(Id: Guid.NewGuid().ToString(),
            DisplayName: Path.GetFileNameWithoutExtension(solutionFilePath),
            ParentId: null,
            FilePath: solutionFilePath,
            LineNumber: null,
            Type: new NodeType.Solution()
    );

}

public class NodeRegistry
{
  private readonly ConcurrentDictionary<string, TestNode> _nodes = new();

  public NodeScopeTracker BeginScope(
      Action<TestNode>? onRegisterTest,
      Action<TestNodeStatusUpdateNotification>? onUpdateStatus) =>
      new(_nodes, onRegisterTest, onUpdateStatus);

  public bool TryGetNode(string nodeId, out TestNode? node) => _nodes.TryGetValue(nodeId, out node);

  public IReadOnlyCollection<TestNode> GetAllNodes() => _nodes.Values.ToList().AsReadOnly();
}


public class NodeScopeTracker(
    ConcurrentDictionary<string, TestNode> globalNodes,
    Action<TestNode>? onRegisterTest,
    Action<TestNodeStatusUpdateNotification>? onUpdateStatus)
{
  public List<TestNode> RegisteredNodes { get; } = [];

  public void RegisterNode(TestNode node)
  {
    RegisteredNodes.Add(node);
    globalNodes[node.Id] = node;
    onRegisterTest?.Invoke(node);
  }

  public void UpdateStatus(TestNode node, TestNodeStatus status)
      => onUpdateStatus?.Invoke(new TestNodeStatusUpdateNotification(node.Id, status));
}