using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Test;
using EasyDotnet.TestRunner.Abstractions;
using EasyDotnet.TestRunner.Models;
using EasyDotnet.TestRunner.Requests;

namespace EasyDotnet.TestRunner.Services;

public class TestRunnerService(
    IMsBuildService msBuildService,
    ISolutionService solutionService,
    ITestSessionRegistry registry,
    IVsTestService vsTestService,
    ITestHierarchyService hierarchyService) : ITestRunner
{
  private bool _isInitialized;
  private TestNode? _solutionNode;

  private readonly List<ProjectTfm> _projectContexts = [];

  public async Task InitializeAsync(string solutionFilePath, CancellationToken ct)
  {
    if (_isInitialized) return;
    using var _ = registry.AcquireLock();
    _solutionNode = CreateSolutionNode(solutionFilePath);
    registry.RegisterNode(_solutionNode);

    registry.UpdateStatus(_solutionNode.Id, new TestNodeStatus.Discovering());

    var projects = solutionService.GetProjectsFromSolutionFile(solutionFilePath);
    var dotnetProjects = await Task.WhenAll(projects
        .Select(x => x.AbsolutePath)
        .Select(x => msBuildService.GetOrSetProjectPropertiesAsync(x, cancellationToken: ct)));

    var testProjects = dotnetProjects.Where(x => x.IsTestProject || x.IsTestingPlatformApplication || x.TestingPlatformDotnetTestSupport);

    foreach (var project in testProjects)
    {
      var projectPath = project.MSBuildProjectFullPath ?? string.Empty;
      var projectName = project.MSBuildProjectName ?? "UnknownProject";

      var tfms = project.TargetFrameworks?.Length > 0
          ? project.TargetFrameworks.Where(t => t != null).ToArray()
          : [project.TargetFramework ?? "netunknown"];

      foreach (var tfm in tfms)
      {
        var context = new ProjectTfm(
            Id: Guid.NewGuid().ToString(),
            ProjectFilePath: projectPath,
            DisplayName: projectName,
            TargetFramework: tfm!
        );

        _projectContexts.Add(context);

        registry.RegisterNode(context.ToTestNode(_solutionNode.Id));
      }
    }

    registry.UpdateStatus(_solutionNode.Id, new TestNodeStatus.Idle());
    _isInitialized = true;
  }

  public TestNode? GetNode(string nodeId) => registry.GetNode(nodeId);

  public async Task StartDiscoveryAsync(CancellationToken ct)
  {
    if (!_isInitialized) throw new InvalidOperationException("Testrunner has not been initialized");

    using var _ = registry.AcquireLock();

    await Task.WhenAll(_projectContexts.Select(async (ctx) =>
    {
      try
      {
        registry.UpdateStatus(ctx.Id, new TestNodeStatus.Discovering());

        var projectProps = await msBuildService.GetOrSetProjectPropertiesAsync(ctx.ProjectFilePath, ctx.TargetFramework, cancellationToken: ct);

        if (projectProps.TestingPlatformDotnetTestSupport)
        {
        }
        else
        {
          var tests = new List<DiscoveredTest>();
          await foreach (var test in vsTestService.DiscoverAsync([projectProps.TargetPath!], ct))
          {
            tests.Add(test);
          }

          hierarchyService.ProcessTestDiscovery(ctx.Id, tests, registry);
        }

        registry.UpdateStatus(ctx.Id, new TestNodeStatus.Idle());
      }
      catch (Exception)
      {
        registry.UpdateStatus(ctx.Id, new TestNodeStatus.Failed("0s", "Uh-oh"));
        throw;
      }
    }));
  }

  public async Task RunTestsAsync(RunRequest request, CancellationToken ct)
  {
    using var _ = registry.AcquireLock();

    var targetNode = registry.GetNode(request.NodeId);
    if (targetNode is null) return;

    if (targetNode.Type is NodeType.Solution)
    {
      foreach (var ctx in _projectContexts)
      {
        await RunProjectBatchAsync(ctx, null, ct);
      }
      return;
    }

    if (targetNode.Type is NodeType.Project)
    {
      var ctx = _projectContexts.FirstOrDefault(p => p.Id == targetNode.Id);
      if (ctx != null)
      {
        await RunProjectBatchAsync(ctx, null, ct);
      }
      return;
    }

    if (targetNode.ProjectId is null) return;

    var projectCtx = _projectContexts.FirstOrDefault(p => p.Id == targetNode.ProjectId);
    if (projectCtx is null) return;

    var testIds = new List<string>();

    if (targetNode.Type is NodeType.TestMethod or NodeType.Subcase)
    {
      testIds.Add(targetNode.Id);
    }
    else
    {
      testIds = [.. registry.GetDescendants(targetNode.Id)
          .Where(n => n.Type is NodeType.TestMethod or NodeType.Subcase)
          .Select(n => n.Id)];
    }

    if (testIds.Count > 0)
    {
      await RunProjectBatchAsync(projectCtx, testIds, ct);
    }
  }

  private async Task RunProjectBatchAsync(ProjectTfm context, List<string>? testIds, CancellationToken ct)
  {
    try
    {
      var props = await msBuildService.GetOrSetProjectPropertiesAsync(
           context.ProjectFilePath, context.TargetFramework, cancellationToken: ct);

      if (string.IsNullOrEmpty(props.TargetPath))
      {
        registry.UpdateStatus(context.Id, new TestNodeStatus.Failed("0ms", "Build failed"));
        return;
      }

      IEnumerable<Guid>? guidIds = testIds?
          .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
          .Where(g => g != Guid.Empty)
          .ToList();

      if (guidIds is null)
      {
        throw new InvalidOperationException("No Guids");
      }

      var results = vsTestService.RunTests(props.TargetPath, guidIds.ToArray());

      foreach (var result in results)
      {
        registry.RegisterTestResult(result);
      }
    }
    catch (Exception ex)
    {
      registry.UpdateStatus(context.Id, new TestNodeStatus.Failed("0ms", ex.Message));
    }
  }

  public async Task DebugTestsAsync(DebugRequest request, CancellationToken ct)
  {
    using var _ = registry.AcquireLock();
    throw new NotImplementedException();
  }

  private static TestNode CreateSolutionNode(string solutionFilePath) => new(
      Id: Guid.NewGuid().ToString(),
      DisplayName: Path.GetFileNameWithoutExtension(solutionFilePath),
      ParentId: null,
      FilePath: solutionFilePath,
      LineNumber: null,
      Type: new NodeType.Solution(),
      ProjectId: null
  );
}