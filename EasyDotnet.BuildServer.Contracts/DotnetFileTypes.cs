namespace EasyDotnet.BuildServer.Contracts;

public static class DotnetFileTypes
{
  public const string SolutionXExtension = ".slnx";
  public const string SolutionExtension = ".sln";
  public const string CsProjectExtension = ".csproj";
  public const string FsProjectExtension = ".fsproj";

  public static bool IsSolutionFile(string path) => MatchExtension(path, SolutionExtension);

  public static bool IsSolutionXFile(string path) => MatchExtension(path, SolutionXExtension);

  public static bool IsAnySolutionFile(string path) => IsSolutionFile(path) || IsSolutionXFile(path);

  public static bool IsCsProjectFile(string path) => MatchExtension(path, CsProjectExtension);

  public static bool IsFsProjectFile(string path) => MatchExtension(path, FsProjectExtension);

  public static bool IsAnyProjectFile(string path) => IsCsProjectFile(path) || IsFsProjectFile(path);

  private static bool MatchExtension(string value, string target) => string.Equals(
      Path.GetExtension(value),
      target,
      StringComparison.OrdinalIgnoreCase);
}