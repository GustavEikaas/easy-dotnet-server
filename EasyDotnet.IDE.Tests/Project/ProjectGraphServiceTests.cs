using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Solution;
using EasyDotnet.IDE.Project.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.SolutionPersistence.Model;

namespace EasyDotnet.IDE.Tests.Project;

public sealed class ProjectGraphServiceTests
{
  [Test]
  public async Task LoadSolutionAsyncRegistersCurrentLoadBeforeReturning()
  {
    var solutionService = new BlockingSolutionService();
    var graph = new ProjectGraphService(
        solutionService,
        workspaceBuildHostManager: null!,
        NullLogger<ProjectGraphService>.Instance);

    var load = graph.LoadSolutionAsync("App.sln", CancellationToken.None);
    var wait = graph.WaitForCurrentLoadAsync(CancellationToken.None);

    await Assert.That(wait.IsCompleted).IsFalse();

    solutionService.Complete("App.sln", []);
    var loadedSnapshot = await load;
    var waitedSnapshot = await wait;

    await Assert.That(waitedSnapshot).IsSameReferenceAs(loadedSnapshot);
    await Assert.That(graph.Snapshot).IsNotNull();
  }

  [Test]
  public async Task WaitForCurrentLoadAsyncCompletesWhenNoLoadWasStarted()
  {
    var graph = new ProjectGraphService(
        new BlockingSolutionService(),
        workspaceBuildHostManager: null!,
        NullLogger<ProjectGraphService>.Instance);

    var snapshot = await graph.WaitForCurrentLoadAsync(CancellationToken.None);

    await Assert.That(snapshot).IsNull();
    await Assert.That(graph.Snapshot).IsNull();
  }

  [Test]
  public async Task OlderOverlappingLoadCannotReplaceNewerSnapshotAndWaitersReceiveExactLoadResult()
  {
    var solutionService = new BlockingSolutionService();
    var graph = new ProjectGraphService(
        solutionService,
        workspaceBuildHostManager: null!,
        NullLogger<ProjectGraphService>.Instance);

    var olderLoad = graph.LoadSolutionAsync("Older.sln", CancellationToken.None);
    var olderWait = graph.WaitForCurrentLoadAsync(CancellationToken.None);
    var newerLoad = graph.LoadSolutionAsync("Newer.sln", CancellationToken.None);
    var newerWait = graph.WaitForCurrentLoadAsync(CancellationToken.None);

    solutionService.Complete("Newer.sln", []);
    var newerSnapshot = await newerLoad;
    solutionService.Complete("Older.sln", []);
    var olderSnapshot = await olderLoad;

    await Assert.That(await olderWait).IsSameReferenceAs(olderSnapshot);
    await Assert.That(await newerWait).IsSameReferenceAs(newerSnapshot);
    await Assert.That(graph.Snapshot).IsSameReferenceAs(newerSnapshot);
    await Assert.That(graph.Snapshot!.SolutionPath).IsEqualTo(Path.GetFullPath("Newer.sln"));
  }

  private sealed class BlockingSolutionService : ISolutionService
  {
    private readonly Dictionary<string, TaskCompletionSource<List<SolutionFileProject>>> _projects = [];

    public void Complete(string solutionFilePath, List<SolutionFileProject> projects) =>
      GetProjectsTask(solutionFilePath).SetResult(projects);

    public Task<List<SolutionFileProject>> GetProjectsFromSolutionFile(
        string solutionFilePath,
        CancellationToken cancellationToken) => GetProjectsTask(solutionFilePath).Task.WaitAsync(cancellationToken);

    public Task<SolutionModel> GetSolutionModelAsync(string solutionFilePath, CancellationToken cancellationToken) =>
      throw new NotSupportedException();

    public Task<bool> AddProjectToSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken) =>
      throw new NotSupportedException();

    public Task<bool> RemoveProjectFromSolutionAsync(string solutionFilePath, string projectPath, CancellationToken cancellationToken) =>
      throw new NotSupportedException();

    private TaskCompletionSource<List<SolutionFileProject>> GetProjectsTask(string solutionFilePath)
    {
      lock (_projects)
      {
        if (!_projects.TryGetValue(solutionFilePath, out var completion))
        {
          completion = new TaskCompletionSource<List<SolutionFileProject>>(TaskCreationOptions.RunContinuationsAsynchronously);
          _projects.Add(solutionFilePath, completion);
        }

        return completion;
      }
    }
  }
}