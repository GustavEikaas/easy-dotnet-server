namespace EasyDotnet.IDE.Workspace.BuildConfiguration;

public static class MsBuildPlatform
{
  public const string SolutionAnyCpu = "Any CPU";
  public const string ProjectAnyCpu = "AnyCPU";

  /// <summary>
  /// Converts a platform string to the form expected when invoking MSBuild on a single project.
  /// Returns <c>null</c> for the project default ("Any CPU" / "AnyCPU"), signalling that
  /// <c>-p:Platform</c> should be omitted — this matches what Visual Studio and Rider do and
  /// avoids MSBuild interpolating "Any CPU" into <c>OutputPath</c> (e.g. <c>bin/Any CPU/Debug/</c>).
  /// </summary>
  public static string? ToProjectPlatform(string? platform)
  {
    if (string.IsNullOrWhiteSpace(platform))
    {
      return null;
    }

    var trimmed = platform.Trim();
    var collapsed = trimmed.Replace(" ", "", System.StringComparison.Ordinal);

    return string.Equals(collapsed, ProjectAnyCpu, System.StringComparison.OrdinalIgnoreCase)
        ? null
        : trimmed;
  }
}
