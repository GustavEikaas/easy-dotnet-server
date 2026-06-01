using EasyDotnet.BuildServer.Contracts;

namespace EasyDotnet.IDE.TestRunner;

internal static class ProjectNodeDisplayName
{
  public static string For(ValidatedDotnetProject project)
  {
    var isMultiTargeted = project.Raw.TargetFrameworks?
        .Where(tfm => !string.IsNullOrWhiteSpace(tfm))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count() > 1;

    return isMultiTargeted
        ? $"{project.ProjectName} ({project.TargetFramework})"
        : project.ProjectName;
  }
}
