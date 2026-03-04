using System.Diagnostics;
using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Execution;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class RestoreHandler
{
  [JsonRpcMethod("projects/restore", UseSingleObjectParameterDeserialization = true)]
  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackages(
      RestoreRequest request,
      [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    ValidateRequest(request);

    foreach (var projectPath in request.ProjectPaths)
    {
      if (cancellationToken.IsCancellationRequested)
      {
        yield break;
      }

      if (!File.Exists(projectPath))
      {
        yield return new RestoreResult(
            projectPath,
            false,
            "Project file not found",
            new RestoreOutput(TimeSpan.Zero, []));
        continue;
      }

      yield return await RestoreProjectAsync(projectPath);
    }
  }

  private static void ValidateRequest(RestoreRequest request)
  {
    if (request.ProjectPaths == null || request.ProjectPaths.Length == 0)
    {
      throw new ArgumentException("ProjectPaths cannot be null or empty", nameof(request));
    }

    for (var i = 0; i < request.ProjectPaths.Length; i++)
    {
      var projectPath = request.ProjectPaths[i];

      if (string.IsNullOrWhiteSpace(projectPath))
      {
        throw new ArgumentException($"ProjectPaths[{i}] cannot be null or whitespace", nameof(request));
      }

      if (!Path.IsPathRooted(projectPath))
      {
        throw new ArgumentException($"ProjectPaths[{i}] must be an absolute path: {projectPath}", nameof(request));
      }
    }
  }

  private static async Task<RestoreResult> RestoreProjectAsync(string projectPath)
  {
    if (!File.Exists(projectPath))
    {
      return new RestoreResult(
          projectPath,
          false,
          $"Project file not found: {projectPath}",
          null);
    }

    try
    {
      var stopwatch = Stopwatch.StartNew();
      var diagnostics = new List<BuildDiagnostic>();

      var logger = new DiagnosticLogger(diagnostics);

      var buildParameters = new BuildParameters
      {
        Loggers = [logger],
      };

      var globalProperties = new Dictionary<string, string>();

      var buildRequest = new BuildRequestData(
          projectPath,
          globalProperties,
          toolsVersion: null,
          ["Restore"],
          hostServices: null);

      using var buildManager = BuildManager.DefaultBuildManager;
      var buildResult = buildManager.Build(buildParameters, buildRequest);

      stopwatch.Stop();

      var success = buildResult.OverallResult == BuildResultCode.Success;

      var output = new RestoreOutput(
          Duration: stopwatch.Elapsed,
          Diagnostics: [.. diagnostics]);

      return new RestoreResult(
          projectPath,
          success,
          success ? null : "Restore failed",
          output);
    }
    catch (Exception ex)
    {
      return new RestoreResult(
          projectPath,
          false,
          $"Exception during restore: {ex.Message}",
          null);
    }
  }
}