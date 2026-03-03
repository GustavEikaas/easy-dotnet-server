namespace EasyDotnet.Domain.Models.Solution;

public sealed record SolutionFileProject(
    string ProjectName,
    string AbsolutePath
);

public static class ProjectFilteringExtensions
{
  /// <summary>
  /// Filters a sequence of SolutionFileProjects to only include .NET compilable projects (.csproj, .fsproj).
  /// </summary>
  public static IEnumerable<SolutionFileProject> OnlyDotnetProjects(this IEnumerable<SolutionFileProject> projects)
  {
    return projects.Where(p => IsDotnetProject(p.AbsolutePath));
  }

  /// <summary>
  /// Filters a sequence of string paths to only include .NET compilable projects (.csproj, .fsproj).
  /// </summary>
  public static IEnumerable<string> OnlyDotnetProjects(this IEnumerable<string> projectPaths)
  {
    return projectPaths.Where(IsDotnetProject);
  }

  /// <summary>
  /// Centralized logic to determine if a file path is a standard .NET project.
  /// </summary>
  private static bool IsDotnetProject(string path)
  {
    return path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase);
  }
}