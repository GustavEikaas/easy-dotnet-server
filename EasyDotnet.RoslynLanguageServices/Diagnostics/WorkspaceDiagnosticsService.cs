using Microsoft.CodeAnalysis;

namespace EasyDotnet.RoslynLanguageServices.Diagnostics;

public static class WorkspaceDiagnosticsService
{
  private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
      ? StringComparison.OrdinalIgnoreCase
      : StringComparison.Ordinal;

  public static async Task<WorkspaceDiagnosticsResponse> GetDiagnosticsAsync(
      Solution solution,
      string? targetPath,
      bool includeWarnings,
      CancellationToken cancellationToken)
  {
    var projects = SelectProjects(solution, targetPath);

    var perProject = await Task.WhenAll(projects.Select(project =>
        Task.Run(() => GetProjectDiagnosticsAsync(project, includeWarnings, cancellationToken), cancellationToken)))
      .ConfigureAwait(false);

    // Multi-targeted projects compile the same file once per target framework
    var diagnostics = perProject
        .SelectMany(static diagnostics => diagnostics)
        .DistinctBy(static d => (d.FilePath, d.Range, d.Code, d.Message))
        .OrderBy(static d => d.FilePath, StringComparer.Ordinal)
        .ThenBy(static d => d.Range.Start.Line)
        .ThenBy(static d => d.Range.Start.Character)
        .ToList();

    return new WorkspaceDiagnosticsResponse(projects.Count, diagnostics);
  }

  private static List<Project> SelectProjects(Solution solution, string? targetPath)
  {
    var projects = solution.Projects.Where(static project => project.SupportsCompilation);

    if (!string.IsNullOrWhiteSpace(targetPath) && IsProjectFile(targetPath))
    {
      var fullTargetPath = Path.GetFullPath(targetPath);
      projects = projects.Where(project =>
          project.FilePath is { Length: > 0 } projectPath &&
          string.Equals(Path.GetFullPath(projectPath), fullTargetPath, PathComparison));
    }

    return [.. projects];
  }

  private static async Task<List<WorkspaceDiagnostic>> GetProjectDiagnosticsAsync(
      Project project,
      bool includeWarnings,
      CancellationToken cancellationToken)
  {
    var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
    if (compilation is null)
    {
      return [];
    }

    return [.. compilation.GetDiagnostics(cancellationToken)
        .Where(diagnostic => ShouldInclude(diagnostic, includeWarnings))
        .Select(diagnostic => ToWorkspaceDiagnostic(diagnostic, project.Name))];
  }

  private static bool ShouldInclude(Diagnostic diagnostic, bool includeWarnings) =>
    !diagnostic.IsSuppressed &&
    diagnostic.Location.IsInSource &&
    !string.IsNullOrEmpty(diagnostic.Location.SourceTree?.FilePath) &&
    (diagnostic.Severity == DiagnosticSeverity.Error ||
     (includeWarnings && diagnostic.Severity == DiagnosticSeverity.Warning));

  private static WorkspaceDiagnostic ToWorkspaceDiagnostic(Diagnostic diagnostic, string projectName)
  {
    var lineSpan = diagnostic.Location.GetLineSpan();
    return new WorkspaceDiagnostic(
        FilePath: diagnostic.Location.SourceTree!.FilePath,
        Range: new WorkspaceDiagnosticRange(
            new WorkspaceDiagnosticPosition(lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character),
            new WorkspaceDiagnosticPosition(lineSpan.EndLinePosition.Line, lineSpan.EndLinePosition.Character)),
        Severity: diagnostic.Severity == DiagnosticSeverity.Error ? 1 : 2,
        Message: diagnostic.GetMessage(),
        Code: diagnostic.Id,
        Source: "roslyn",
        Category: diagnostic.Descriptor.Category,
        Project: projectName);
  }

  private static bool IsProjectFile(string path) =>
    path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
    path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
}