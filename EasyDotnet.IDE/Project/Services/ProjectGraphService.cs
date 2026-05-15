using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Solution;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Project.Services;

public sealed record ProjectGraphSnapshot(
  string SolutionPath,
  IReadOnlyList<SolutionFileProject> SolutionProjects,
  IReadOnlyList<ValidatedDotnetProject> EvaluatedProjects,
  IReadOnlyList<ProjectEvaluationResult> FailedEvaluations);

public class ProjectGraphService(
  ISolutionService solutionService,
  WorkspaceBuildHostManager workspaceBuildHostManager,
  ILogger<ProjectGraphService> logger)
{
  private readonly object _sync = new();
  private ProjectGraphSnapshot? _snapshot;

  public ProjectGraphSnapshot? Snapshot
  {
    get
    {
      lock (_sync)
      {
        return _snapshot;
      }
    }
  }

  public async Task LoadSolutionAsync(string solutionFile, CancellationToken ct = default)
  {
    var solutionProjects = await solutionService.GetProjectsFromSolutionFile(solutionFile, ct);
    var projectPaths = solutionProjects.ConvertAll(x => x.AbsolutePath);
    var projectNames = solutionProjects.ConvertAll(x => x.ProjectName);

    logger.LogInformation("Loading project graph for {Count} projects:\n{Projects}",
        projectNames.Count,
        string.Join("\n  - ", projectNames.Prepend("")));

    var evaluatedProjects = new List<ValidatedDotnetProject>();
    var failedEvaluations = new List<ProjectEvaluationResult>();

    if (projectPaths.Count > 0)
    {
      await foreach (var result in workspaceBuildHostManager.GetProjectPropertiesBatchAsync(
          new GetProjectPropertiesBatchRequest([.. projectPaths], Configuration: null),
          ct))
      {
        if (result.Success && result.Project is not null)
        {
          evaluatedProjects.Add(result.Project);
        }
        else
        {
          failedEvaluations.Add(result);
          logger.LogWarning(
              "Failed to evaluate {ProjectPath} ({TFM}): {Error}",
              result.ProjectPath,
              result.TargetFramework ?? "unknown",
              result.Error?.Message);
        }
      }
    }

    var snapshot = new ProjectGraphSnapshot(
        Path.GetFullPath(solutionFile),
        solutionProjects.AsReadOnly(),
        evaluatedProjects.AsReadOnly(),
        failedEvaluations.AsReadOnly());

    lock (_sync)
    {
      _snapshot = snapshot;
    }

    logger.LogInformation(
        "Finished loading project graph for {SolutionFile}: {EvaluatedCount} evaluated, {FailedCount} failed",
        solutionFile,
        evaluatedProjects.Count,
        failedEvaluations.Count);
  }
}
