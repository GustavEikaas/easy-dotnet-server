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
  public async Task RestoreNugetPackagesAsync_SolutionTarget_PassesSolutionThroughAndAppliesActiveSolutionConfiguration()
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
    await Assert.That(inner.RestoreRequests[0].ProjectPaths).IsEquivalentTo([solutionPath]);
    await Assert.That(inner.RestoreRequests[0].Configuration).IsEqualTo("Debug");
    await Assert.That(inner.RestoreRequests[0].Platform).IsEqualTo("Any CPU");
  }

  [Test]
  public async Task BatchBuildAsync_SolutionTarget_PassesSolutionThroughAndAppliesActiveSolutionConfiguration()
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
    await Assert.That(inner.BuildRequests[0].ProjectPaths).IsEquivalentTo([solutionPath]);
    await Assert.That(inner.BuildRequests[0].Configuration).IsEqualTo("Debug");
    await Assert.That(inner.BuildRequests[0].Platform).IsEqualTo("Any CPU");
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

  private static WorkspaceBuildHostManager CreateManager(
      CapturingBuildHostManager inner,
      ISolutionService solutionService,
      ResolvedBuildConfiguration resolvedConfiguration)
  {
    return new WorkspaceBuildHostManager(
        solutionService,
        inner,
        new ProjectEvaluationCache(NullLogger<ProjectEvaluationCache>.Instance),
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
        Task.FromResult(Resolve(targetPath));

    public Task<IReadOnlyList<ResolvedBuildConfiguration>> ResolveTargetsAsync(
        IReadOnlyCollection<string> targetPaths,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ResolvedBuildConfiguration>>([.. targetPaths.Select(Resolve)]);

    public Task SetActiveConfigurationAsync(WorkspaceBuildConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private ResolvedBuildConfiguration Resolve(string targetPath)
    {
      if (DotnetFileTypes.IsAnySolutionFile(targetPath))
      {
        return resolvedConfiguration with
        {
          TargetPath = targetPath,
          Platform = resolvedConfiguration.WorkspaceConfiguration.Platform,
          UsedProjectMapping = false
        };
      }

      return resolvedConfiguration with { TargetPath = targetPath };
    }
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
          Success: true,
          ErrorMessage: null,
          Output: new RestoreOutput(TimeSpan.Zero, []));
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

    public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async IAsyncEnumerable<T> Empty<T>()
    {
      await Task.CompletedTask;
      yield break;
    }
  }
}