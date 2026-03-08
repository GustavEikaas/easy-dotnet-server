using System.Diagnostics;
using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Execution;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class RestoreHandler
{
  private static readonly BuildManager BuildManager = BuildManager.DefaultBuildManager;
  private static readonly SemaphoreSlim BuildLock = new(1, 1);

  [JsonRpcMethod("projects/restore", UseSingleObjectParameterDeserialization = true)]
  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackages(
      RestoreRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    ValidateRequest(request);

    await BuildLock.WaitAsync(ct);
    var buildParameters = new BuildParameters
    {
      EnableNodeReuse = true,
      MaxNodeCount = Environment.ProcessorCount,
    };

    BuildManager.BeginBuild(buildParameters);
    try
    {
      foreach (var projectPath in request.ProjectPaths)
      {
        if (ct.IsCancellationRequested) { yield break; }

        if (!File.Exists(projectPath))
        {
          yield return new RestoreResult(
              projectPath,
              Success: false,
              ErrorMessage: "Project file not found",
              Output: new RestoreOutput(TimeSpan.Zero, []));
          continue;
        }

        yield return await RestoreProjectAsync(projectPath, ct);
      }
    }
    finally
    {
      BuildManager.EndBuild();
      BuildLock.Release();
    }
  }

  private static Task<RestoreResult> RestoreProjectAsync(string projectPath, CancellationToken ct) =>
      Task.Run(() =>
      {
        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<BuildDiagnostic>();

        try
        {
          ct.ThrowIfCancellationRequested();

          var buildRequest = new BuildRequestData(
                projectPath,
                globalProperties: new Dictionary<string, string>(),
                toolsVersion: null,
                targetsToBuild: ["Restore"],
                hostServices: null);

          var submission = BuildManager.PendBuildRequest(buildRequest);
          submission.ExecuteAsync(null, null);
          var result = submission.WaitHandle.WaitOne()
                ? submission.BuildResult
                : throw new InvalidOperationException("Restore submission did not complete");

          stopwatch.Stop();

          var success = result.OverallResult == BuildResultCode.Success;
          return new RestoreResult(
                projectPath,
                Success: success,
                ErrorMessage: success ? null : "Restore failed",
                Output: new RestoreOutput(stopwatch.Elapsed, [.. diagnostics]));
        }
        catch (OperationCanceledException)
        {
          stopwatch.Stop();
          return new RestoreResult(
                projectPath,
                Success: false,
                ErrorMessage: "Cancelled",
                Output: new RestoreOutput(stopwatch.Elapsed, [.. diagnostics]));
        }
        catch (Exception ex)
        {
          stopwatch.Stop();
          return new RestoreResult(
                projectPath,
                Success: false,
                ErrorMessage: $"Exception during restore: {ex.Message}",
                Output: new RestoreOutput(stopwatch.Elapsed, [.. diagnostics]));
        }
      }, ct);

  private static void ValidateRequest(RestoreRequest request)
  {
    if (request.ProjectPaths is null || request.ProjectPaths.Length == 0)
      throw new ArgumentException("ProjectPaths cannot be null or empty", nameof(request));

    for (var i = 0; i < request.ProjectPaths.Length; i++)
    {
      if (string.IsNullOrWhiteSpace(request.ProjectPaths[i]))
      {
        throw new ArgumentException($"ProjectPaths[{i}] cannot be null or whitespace", nameof(request));
      }

      if (!Path.IsPathRooted(request.ProjectPaths[i]))
      {
        throw new ArgumentException($"ProjectPaths[{i}] must be an absolute path: {request.ProjectPaths[i]}", nameof(request));
      }

    }
  }
}