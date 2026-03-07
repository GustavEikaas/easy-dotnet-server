using System.Diagnostics;
using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Execution;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class BatchBuildHandler
{
  [JsonRpcMethod("projects/batchBuild", UseSingleObjectParameterDeserialization = true)]
  public async IAsyncEnumerable<BatchBuildResult> BatchBuildAsync(
      BatchBuildRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    ValidateRequest(request);

    foreach (var projectPath in request.ProjectPaths)
    {
      if (ct.IsCancellationRequested)
        yield break;

      if (!File.Exists(projectPath))
      {
        yield return new BatchBuildResult(
            projectPath,
            Kind: BatchBuildResultKind.Finished,
            Success: false,
            ErrorMessage: "Project file not found",
            Output: new BatchBuildOutput(TimeSpan.Zero, []));
        continue;
      }

      yield return new BatchBuildResult(
          projectPath,
          Kind: BatchBuildResultKind.Started,
          Success: null,
          ErrorMessage: null,
          Output: null);

      yield return await BuildProjectAsync(projectPath, request, ct);
    }
  }

  private static void ValidateRequest(BatchBuildRequest request)
  {
    if (request.ProjectPaths is null || request.ProjectPaths.Length == 0)
      throw new ArgumentException("ProjectPaths cannot be null or empty", nameof(request));

    for (var i = 0; i < request.ProjectPaths.Length; i++)
    {
      var path = request.ProjectPaths[i];
      if (string.IsNullOrWhiteSpace(path))
      {
        throw new ArgumentException($"ProjectPaths[{i}] cannot be null or whitespace", nameof(request));
      }

      if (!Path.IsPathRooted(path))
      {
        throw new ArgumentException($"ProjectPaths[{i}] must be an absolute path: {path}", nameof(request));
      }
    }
  }

  private static Task<BatchBuildResult> BuildProjectAsync(
      string projectPath,
      BatchBuildRequest request,
      CancellationToken ct)
  {
    return Task.Run(() =>
    {
      var stopwatch = Stopwatch.StartNew();
      var diagnostics = new List<BuildDiagnostic>();

      try
      {
        ct.ThrowIfCancellationRequested();

        var globalProperties = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(request.Configuration))
        {
          globalProperties["Configuration"] = request.Configuration!;
        }

        if (!string.IsNullOrEmpty(request.TargetFramework))
        {
          globalProperties["TargetFramework"] = request.TargetFramework!;
        }

        var logger = new DiagnosticLogger(diagnostics);
        var buildParameters = new BuildParameters
        {
          Loggers = [logger],
        };

        var buildRequest = new BuildRequestData(
                projectPath,
                globalProperties,
                toolsVersion: null,
                [request.BuildTarget ?? "Build"],
                hostServices: null);

        using var buildManager = new BuildManager();
        var result = buildManager.Build(buildParameters, buildRequest);

        stopwatch.Stop();

        var success = result.OverallResult == BuildResultCode.Success;
        return new BatchBuildResult(
                projectPath,
                Kind: BatchBuildResultKind.Finished,
                Success: success,
                ErrorMessage: success ? null : "Build failed",
                Output: new BatchBuildOutput(stopwatch.Elapsed, [.. diagnostics]));
      }
      catch (OperationCanceledException)
      {
        stopwatch.Stop();
        return new BatchBuildResult(
                projectPath,
                Kind: BatchBuildResultKind.Finished,
                Success: false,
                ErrorMessage: "Cancelled",
                Output: new BatchBuildOutput(stopwatch.Elapsed, [.. diagnostics]));
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        return new BatchBuildResult(
                projectPath,
                Kind: BatchBuildResultKind.Finished,
                Success: false,
                ErrorMessage: $"Exception during build: {ex.Message}",
                Output: new BatchBuildOutput(stopwatch.Elapsed, [.. diagnostics]));
      }
    }, ct);
  }
}