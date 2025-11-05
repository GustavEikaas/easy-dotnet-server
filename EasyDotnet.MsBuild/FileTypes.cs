namespace EasyDotnet.MsBuild;

public static class FileTypes
{
  public const string SolutionXExtension = ".slnx";
  public const string SolutionExtension = ".sln";

  public const string CsProjectExtension = ".csproj";
  public const string CsFileExtension = ".cs";

  public const string FsProjectExtension = ".fsproj";
  public const string FsFileExtension = ".fs";

  /// <summary>
  /// Checks whether the given path points to a Visual Studio solution sln file
  /// </summary>
  public static bool IsSolutionFile(string slnPath) => MatchExtension(slnPath, SolutionExtension);

  /// <summary>
  /// Checks whether the given path points to a Visual Studio slnx file
  /// </summary>
  public static bool IsSolutionXFile(string slnPath) => MatchExtension(slnPath, SolutionXExtension);

  /// <summary>
  /// Checks whether the given path points to a Visual Studio solution file (.sln or .slnx).
  /// </summary>
  public static bool IsAnySolutionFile(string slnPath) => IsSolutionFile(slnPath) || IsSolutionXFile(slnPath);

  /// <summary>
  /// Checks whether the given path points to a C# project file (.csproj).
  /// </summary>
  public static bool IsCsProjectFile(string filePath) => MatchExtension(filePath, CsProjectExtension);

  /// <summary>
  /// Checks whether the given path points to a F# project file (.fsproj).
  /// </summary>
  public static bool IsFsProjectFile(string filePath) => MatchExtension(filePath, FsProjectExtension);

  /// <summary>
  /// Checks whether the given path points to a .NET project file (.fsproj or .csproj).
  /// </summary>
  public static bool IsAnyProjectFile(string filePath) => IsCsProjectFile(filePath) || IsFsProjectFile(filePath);

  /// <summary>
  /// Checks whether the given path points to a C# source file (.cs).
  /// </summary>
  public static bool IsCsFile(string filePath) => MatchExtension(filePath, CsFileExtension);

  /// <summary>
  /// Checks whether the given path points to a C# source file (.cs).
  /// </summary>
  public static bool IsFsFile(string filePath) => MatchExtension(filePath, FsFileExtension);

  private static bool MatchExtension(string value, string target) => string.Equals(
        Path.GetExtension(value),
        target,
        StringComparison.OrdinalIgnoreCase);
}