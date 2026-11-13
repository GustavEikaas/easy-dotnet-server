using System.ComponentModel;
using System.Diagnostics;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Workspace.Services;
using ModelContextProtocol.Server;

namespace EasyDotnet.IDE.Mcp.Tools;

[McpServerToolType]
public sealed class BuildTools(WorkspaceBuildService buildService, McpWorkspace workspace)
{
  [McpServerTool(Name = "build", Title = "Build", Idempotent = true, OpenWorld = false)]
  [Description("""
    Builds a solution or project with MSBuild in a long-lived build server and returns the
    diagnostics as JSON: success, error and warning counts, and each error with file, line,
    column, code and message. Prefer this over `dotnet build`: the output is already
    deduplicated and structured. Defaults to the workspace solution.
    """)]
  public Task<string> BuildAsync(
      [Description("Absolute or workspace-relative path to a .sln, .slnx, .csproj or .fsproj. Defaults to the workspace solution.")] string? target = null,
      [Description("MSBuild configuration, e.g. Debug or Release. Defaults to the project's own default.")] string? configuration = null,
      [Description("Include warnings in the result. Errors are always included.")] bool includeWarnings = false,
      [Description("Maximum diagnostics to return per severity.")] int maxDiagnostics = 50,
      CancellationToken ct = default) => McpErrors.SurfaceAsync(async () =>
  {
    var targetPath = workspace.ResolveBuildTarget(target);
    var stopwatch = Stopwatch.StartNew();
    var run = await buildService.RunBuildTargetAsync(
        targetPath,
        Path.GetFileName(targetPath),
        configuration,
        buildTarget: "Build",
        operationName: "Build",
        platform: null,
        restoreBeforeOperation: true,
        ct);
    stopwatch.Stop();

    var errors = Distinct(run.Diagnostics, BuildDiagnosticSeverity.Error);
    var warnings = Distinct(run.Diagnostics, BuildDiagnosticSeverity.Warning);

    return McpJson.Serialize(new
    {
      success = run.Success && errors.Count == 0,
      target = targetPath,
      stage = run.RestoreFailed ? "restore" : "build",
      durationMs = stopwatch.ElapsedMilliseconds,
      errorCount = errors.Count,
      warningCount = warnings.Count,
      errors = errors.Take(maxDiagnostics).Select(ToDto),
      warnings = includeWarnings ? warnings.Take(maxDiagnostics).Select(ToDto) : null,
      failures = run.Failures.Count > 0 ? run.Failures : null,
      truncated = errors.Count > maxDiagnostics || (includeWarnings && warnings.Count > maxDiagnostics) ? true : (bool?)null,
    });
  });

  private static List<BuildDiagnostic> Distinct(IEnumerable<BuildDiagnostic> diagnostics, BuildDiagnosticSeverity severity) =>
      [.. diagnostics
          .Where(d => d.Severity == severity)
          .DistinctBy(d => (d.File, d.LineNumber, d.ColumnNumber, d.Code, d.Message))];

  private static object ToDto(BuildDiagnostic d) => new
  {
    file = d.File,
    line = d.LineNumber,
    column = d.ColumnNumber,
    code = d.Code,
    message = d.Message,
    project = d.ProjectFile is null ? null : Path.GetFileName(d.ProjectFile),
  };
}