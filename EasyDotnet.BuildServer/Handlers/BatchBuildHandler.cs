using System.Diagnostics;
using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.Logging;
using EasyDotnet.BuildServer.MsBuildProject.Cache;
using Microsoft.Build.Execution;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class BatchBuildHandler(BuildFreshnessChecker freshnessChecker, Logger logger)
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

      var (hit, missReason) = TryFastUpToDate(projectPath, request);
      if (hit is not null)
      {
        yield return hit;
        continue;
      }

      var buildStartedAtUtc = DateTime.UtcNow;
      var real = await BuildProjectAsync(projectPath, request, ct);
      RecordSuccessfulBuildState(projectPath, request, real, buildStartedAtUtc);
      if (missReason is not null && real.Output is not null)
      {
        var diags = new List<BuildDiagnostic>(real.Output.Diagnostics)
        {
          new BuildDiagnostic(
              File: null,
              LineNumber: 0,
              ColumnNumber: 0,
              EndLineNumber: 0,
              EndColumnNumber: 0,
              Message: $"FUTD declined: {missReason}",
              Code: "ED-FUTD-MISS",
              ProjectFile: projectPath,
              Severity: BuildDiagnosticSeverity.Message),
        };
        real = real with { Output = new BatchBuildOutput(real.Output.Duration, [.. diags]) };
      }
      yield return real;
    }
  }

  private void RecordSuccessfulBuildState(string projectPath, BatchBuildRequest request, BatchBuildResult result, DateTime startedAtUtc)
  {
    if (result.Success != true)
    {
      return;
    }

    var target = request.BuildTarget ?? "Build";
    if (!string.Equals(target, "Build", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    if (string.IsNullOrEmpty(request.TargetFramework))
    {
      return;
    }

    freshnessChecker.RecordSuccessfulBuild(
        projectPath,
        request.Configuration,
        request.Platform,
        request.TargetFramework!,
        startedAtUtc);
  }

  private (BatchBuildResult? Hit, string? MissReason) TryFastUpToDate(string projectPath, BatchBuildRequest request)
  {
    if (request.RestoreBeforeBuild)
    {
      logger.LogDebug("FUTD skipped: restore-before-build requested for {Project}", projectPath);
      return (null, null);
    }

    var target = request.BuildTarget ?? "Build";
    if (!string.Equals(target, "Build", StringComparison.OrdinalIgnoreCase))
    {
      logger.LogDebug("FUTD skipped: target={Target} for {Project}", target, projectPath);
      return (null, null);
    }
    if (string.IsNullOrEmpty(request.TargetFramework))
    {
      logger.LogDebug("FUTD skipped: no TargetFramework specified for {Project}", projectPath);
      return (null, null);
    }

    try
    {
      var result = freshnessChecker.Check(projectPath, request.Configuration, request.Platform, request.TargetFramework!);
      if (!result.IsUpToDate)
      {
        logger.LogInformation("FUTD declined: {Project} ({Tfm}) — {Reason}", projectPath, request.TargetFramework, result.Reason);
        return (null, result.Reason);
      }

      var hit = new BatchBuildResult(
          projectPath,
          Kind: BatchBuildResultKind.Finished,
          Success: true,
          ErrorMessage: null,
          Output: new BatchBuildOutput(
              TimeSpan.Zero,
              [new BuildDiagnostic(
                  File: null,
                  LineNumber: 0,
                  ColumnNumber: 0,
                  EndLineNumber: 0,
                  EndColumnNumber: 0,
                  Message: "Project is up-to-date (build skipped via FUTD). Set <DisableFastUpToDateCheck>true</DisableFastUpToDateCheck> to disable.",
                  Code: "ED-FUTD",
                  ProjectFile: projectPath,
                  Severity: BuildDiagnosticSeverity.Message)]));
      return (hit, null);
    }
    catch (Exception ex)
    {
      return (null, $"FUTD check threw: {ex.Message}");
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

        if (!string.IsNullOrEmpty(request.Platform))
        {
          globalProperties["Platform"] = request.Platform!;
        }

        if (!string.IsNullOrEmpty(request.TargetFramework))
        {
          globalProperties["TargetFramework"] = request.TargetFramework!;
        }

        var logger = new DiagnosticLogger(diagnostics);
        var buildParameters = BuildServerBuildParameters.Create([logger]);

        var buildRequest = new BuildRequestData(
                projectPath,
                globalProperties,
                toolsVersion: null,
                ResolveTargetsToBuild(request),
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
                Output: new BatchBuildOutput(stopwatch.Elapsed, BuildDiagnosticDeduplicator.Deduplicate(diagnostics)));
      }
      catch (OperationCanceledException)
      {
        stopwatch.Stop();
        return new BatchBuildResult(
                projectPath,
                Kind: BatchBuildResultKind.Finished,
                Success: false,
                ErrorMessage: "Cancelled",
                Output: new BatchBuildOutput(stopwatch.Elapsed, BuildDiagnosticDeduplicator.Deduplicate(diagnostics)));
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        return new BatchBuildResult(
                projectPath,
                Kind: BatchBuildResultKind.Finished,
                Success: false,
                ErrorMessage: $"Exception during build: {ex.Message}",
                Output: new BatchBuildOutput(stopwatch.Elapsed, BuildDiagnosticDeduplicator.Deduplicate(diagnostics)));
      }
    }, ct);
  }

  private static string[] ResolveTargetsToBuild(BatchBuildRequest request)
  {
    var target = request.BuildTarget ?? "Build";
    return request.RestoreBeforeBuild && string.Equals(target, "Build", StringComparison.OrdinalIgnoreCase)
        ? ["Restore", "Build"]
        : [target];
  }
}