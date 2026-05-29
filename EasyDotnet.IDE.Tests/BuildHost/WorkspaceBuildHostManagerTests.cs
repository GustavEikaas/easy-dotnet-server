using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Solution;
using EasyDotnet.IDE.Workspace.BuildConfiguration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.SolutionPersistence.Model;

namespace EasyDotnet.IDE.Tests.BuildHost;

public sealed class WorkspaceBuildHostManagerTests
{
  [Test]
  public async Task RestoreNugetPackagesAsync_SolutionTarget_ExpandsToProjectsAndAppliesActiveSolutionMapping()
  {
    var solutionPath = Path.Combine(Path.GetTempPath(), "App.sln");
    var projectPath = Path.Combine(Path.GetTempPath(), "App.csproj");
    var inner = new CapturingBuildHostManager();
    var manager = CreateManager(
        inner,
        new NoOpSolutionService([new SolutionFileProject("App", projectPath)]),
        new ResolvedBuildConfiguration(
            projectPath,
            new WorkspaceBuildConfiguration("Debug", "Any CPU"),
            "Debug",
            null,
            Build: true,
            Deploy: false,
            UsedProjectMapping: true));

    await manager.RestoreNugetPackagesAsync(new RestoreRequest([solutionPath]), CancellationToken.None).ToListAsync(CancellationToken.None);

    await Assert.That(inner.RestoreRequests.Count).IsEqualTo(1);
    await Assert.That(inner.RestoreRequests[0].ProjectPaths).IsEquivalentTo([projectPath]);
    await Assert.That(inner.RestoreRequests[0].Configuration).IsEqualTo("Debug");
    await Assert.That(inner.RestoreRequests[0].Platform).IsNull();
  }

  [Test]
  public async Task BatchBuildAsync_SolutionTarget_ExpandsToProjectsAndAppliesActiveSolutionMapping()
  {
    var solutionPath = Path.Combine(Path.GetTempPath(), "App.sln");
    var projectPath = Path.Combine(Path.GetTempPath(), "App.csproj");
    var inner = new CapturingBuildHostManager();
    var manager = CreateManager(
        inner,
        new NoOpSolutionService([new SolutionFileProject("App", projectPath)]),
        new ResolvedBuildConfiguration(
            projectPath,
            new WorkspaceBuildConfiguration("Debug", "Any CPU"),
            "Debug",
            null,
            Build: true,
            Deploy: false,
            UsedProjectMapping: true));

    await manager.BatchBuildAsync(new BatchBuildRequest([solutionPath], Configuration: null), CancellationToken.None).ToListAsync(CancellationToken.None);

    await Assert.That(inner.BuildRequests.Count).IsEqualTo(1);
    await Assert.That(inner.BuildRequests[0].ProjectPaths).IsEquivalentTo([projectPath]);
    await Assert.That(inner.BuildRequests[0].Configuration).IsEqualTo("Debug");
    await Assert.That(inner.BuildRequests[0].Platform).IsNull();
  }

  [Test]
  public async Task RestoreNugetPackagesAsync_ProjectTarget_OmitsAnyCpuPlatform()
  {
    var projectPath = Path.Combine(Path.GetTempPath(), "App.csproj");
    var inner = new CapturingBuildHostManager();
    var manager = CreateManager(
        inner,
        new NoOpSolutionService(),
        new ResolvedBuildConfiguration(
            projectPath,
            new WorkspaceBuildConfiguration("Debug", "Any CPU"),
            "Debug",
            null,
            Build: true,
            Deploy: false,
            UsedProjectMapping: true));

    await manager.RestoreNugetPackagesAsync(new RestoreRequest([projectPath]), CancellationToken.None).ToListAsync(CancellationToken.None);

    await Assert.That(inner.RestoreRequests.Count).IsEqualTo(1);
    await Assert.That(inner.RestoreRequests[0].ProjectPaths).IsEquivalentTo([projectPath]);
    await Assert.That(inner.RestoreRequests[0].Configuration).IsEqualTo("Debug");
    await Assert.That(inner.RestoreRequests[0].Platform).IsNull();
  }

  [Test]
  public async Task RestoreNugetPackagesAsync_NoOpRestore_DoesNotClearEvaluationCache()
  {
    var projectPath = Path.Combine(Path.GetTempPath(), "App.csproj");
    var cache = new ProjectEvaluationCache(NullLogger<ProjectEvaluationCache>.Instance);
    var inner = new CapturingBuildHostManager { RestoreNoOp = true };
    var manager = CreateManager(
        inner,
        new NoOpSolutionService(),
        new ResolvedBuildConfiguration(
            projectPath,
            new WorkspaceBuildConfiguration("Debug", "Any CPU"),
            "Debug",
            null,
            Build: true,
            Deploy: false,
            UsedProjectMapping: true),
        cache);

    var (_, firstIsNew) = cache.GetOrRegister(projectPath, "Debug", null);
    await Assert.That(firstIsNew).IsTrue();

    await manager.RestoreNugetPackagesAsync(new RestoreRequest([projectPath]), CancellationToken.None).ToListAsync(CancellationToken.None);

    var (_, secondIsNew) = cache.GetOrRegister(projectPath, "Debug", null);
    await Assert.That(secondIsNew).IsFalse();
  }

  [Test]
  public async Task RestoreNugetPackagesAsync_NonNoOpRestore_ClearsEvaluationCache()
  {
    var projectPath = Path.Combine(Path.GetTempPath(), "App.csproj");
    var cache = new ProjectEvaluationCache(NullLogger<ProjectEvaluationCache>.Instance);
    var inner = new CapturingBuildHostManager { RestoreNoOp = false };
    var manager = CreateManager(
        inner,
        new NoOpSolutionService(),
        new ResolvedBuildConfiguration(
            projectPath,
            new WorkspaceBuildConfiguration("Debug", "Any CPU"),
            "Debug",
            null,
            Build: true,
            Deploy: false,
            UsedProjectMapping: true),
        cache);

    var (_, firstIsNew) = cache.GetOrRegister(projectPath, "Debug", null);
    await Assert.That(firstIsNew).IsTrue();

    await manager.RestoreNugetPackagesAsync(new RestoreRequest([projectPath]), CancellationToken.None).ToListAsync(CancellationToken.None);

    var (_, secondIsNew) = cache.GetOrRegister(projectPath, "Debug", null);
    await Assert.That(secondIsNew).IsTrue();
  }

  [Test]
  public async Task RestoreNugetPackagesAsync_FailedRestore_ClearsEvaluationCache()
  {
    var projectPath = Path.Combine(Path.GetTempPath(), "App.csproj");
    var cache = new ProjectEvaluationCache(NullLogger<ProjectEvaluationCache>.Instance);
    var inner = new CapturingBuildHostManager { RestoreSuccess = false, RestoreNoOp = true };
    var manager = CreateManager(
        inner,
        new NoOpSolutionService(),
        new ResolvedBuildConfiguration(
            projectPath,
            new WorkspaceBuildConfiguration("Debug", "Any CPU"),
            "Debug",
            null,
            Build: true,
            Deploy: false,
            UsedProjectMapping: true),
        cache);

    var (_, firstIsNew) = cache.GetOrRegister(projectPath, "Debug", null);
    await Assert.That(firstIsNew).IsTrue();

    await manager.RestoreNugetPackagesAsync(new RestoreRequest([projectPath]), CancellationToken.None).ToListAsync(CancellationToken.None);

    var (_, secondIsNew) = cache.GetOrRegister(projectPath, "Debug", null);
    await Assert.That(secondIsNew).IsTrue();
  }

  private static WorkspaceBuildHostManager CreateManager(
      CapturingBuildHostManager inner,
      ISolutionService solutionService,
      ResolvedBuildConfiguration resolvedConfiguration,
      ProjectEvaluationCache? cache = null)
  {
    return new WorkspaceBuildHostManager(
        solutionService,
        inner,
        cache ?? new ProjectEvaluationCache(NullLogger<ProjectEvaluationCache>.Instance),
        new StubWorkspaceBuildConfigurationService(resolvedConfiguration),
        NullLogger<WorkspaceBuildHostManager>.Instance);
  }

  private sealed class StubWorkspaceBuildConfigurationService(
      ResolvedBuildConfiguration resolvedConfiguration) : IWorkspaceBuildConfigurationService
  {
#pragma warning disable CS0067
    public event Action<WorkspaceBuildConfigurationChangedEventArgs>? ConfigurationChanged;
#pragma warning restore CS0067

    public Task<WorkspaceBuildConfiguration> GetActiveConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(resolvedConfiguration.WorkspaceConfiguration);

    public Task<IReadOnlyList<WorkspaceBuildConfiguration>> GetAvailableConfigurationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkspaceBuildConfiguration>>([resolvedConfiguration.WorkspaceConfiguration]);

    public Task<ResolvedBuildConfiguration> ResolveTargetAsync(string targetPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(resolvedConfiguration);

    public Task<IReadOnlyList<ResolvedBuildConfiguration>> ResolveTargetsAsync(
        IReadOnlyCollection<string> targetPaths,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ResolvedBuildConfiguration>>([resolvedConfiguration]);

    public Task SetActiveConfigurationAsync(WorkspaceBuildConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
  }

  private sealed class NoOpSolutionService(
      IReadOnlyList<SolutionFileProject>? solutionProjects = null) : ISolutionService
  {
    public Task<SolutionModel> GetSolutionModelAsync(string solutionFilePath, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<List<SolutionFileProject>> GetProjectsFromSolutionFile(string solutionFilePath, CancellationToken cancellationToken) =>
        Task.FromResult((solutionProjects ?? []).ToList());

    public Task<bool> AddProjectToSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> RemoveProjectFromSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken) =>
        Task.FromResult(false);
  }

  private sealed class CapturingBuildHostManager : IBuildHostManager
  {
    public List<RestoreRequest> RestoreRequests { get; } = [];
    public List<BatchBuildRequest> BuildRequests { get; } = [];
    public bool RestoreSuccess { get; init; } = true;
    public bool RestoreNoOp { get; init; }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatchAsync(
        GetProjectPropertiesBatchRequest request,
        CancellationToken cancellationToken) =>
        Empty<ProjectEvaluationResult>();

    public Task<GetWatchListResponse> GetProjectWatchListAsync(GetWatchListRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new GetWatchListResponse([]));

    public async IAsyncEnumerable<RestoreResult> RestoreNugetPackagesAsync(
        RestoreRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
      RestoreRequests.Add(request);
      yield return new RestoreResult(
          request.ProjectPaths[0],
          Success: RestoreSuccess,
          ErrorMessage: RestoreSuccess ? null : "Restore failed",
          Output: new RestoreOutput(TimeSpan.Zero, [], RestoreNoOp, RestoreNoOp ? "Restore artifacts were unchanged." : null));
      await Task.CompletedTask;
    }

    public async IAsyncEnumerable<BatchBuildResult> BatchBuildAsync(
        BatchBuildRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
      BuildRequests.Add(request);
      yield return new BatchBuildResult(
          request.ProjectPaths[0],
          BatchBuildResultKind.Finished,
          Success: true,
          ErrorMessage: null,
          Output: new BatchBuildOutput(TimeSpan.Zero, []));
      await Task.CompletedTask;
    }

    public Task<ConvertSingleFileResponse> ConvertFileToProjectAsync(string entryPointFilePath, CancellationToken cancellationToken) =>
        Task.FromResult(new ConvertSingleFileResponse(
            "",
            new ProjectEvaluationResult(entryPointFilePath, null, null, false, null, null)));

    public Task<BuildServerDiagnosticsResponse> GetBuildServerDiagnosticsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BuildServerDiagnosticsResponse("", 0, "", ""));

    public Task<InstalledPackageReference[]> ListPackageReferencesAsync(string projectPath, CancellationToken cancellationToken) =>
        Task.FromResult(Array.Empty<InstalledPackageReference>());

    public Task SetLogLevelAsync(string level, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string[]> GetLogsAsync(CancellationToken cancellationToken) => Task.FromResult(Array.Empty<string>());

    private static async IAsyncEnumerable<T> Empty<T>()
    {
      await Task.CompletedTask;
      yield break;
    }
  }
}
