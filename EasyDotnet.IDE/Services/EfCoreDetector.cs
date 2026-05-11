using Microsoft.Extensions.DependencyModel;

namespace EasyDotnet.IDE.Services;

/// <summary>
/// Decides whether to auto-attach the profiler to a project by inspecting its build output for
/// EF Core. We read the deps.json (the runtime deployment manifest produced by MSBuild) using
/// Microsoft.Extensions.DependencyModel, which captures every assembly that will be loadable
/// at runtime — including ones pulled in transitively through ProjectReference chains. This
/// catches the case where EF Core lives in a referenced project rather than being a direct
/// PackageReference on the executable.
/// </summary>
public static class EfCoreDetector
{
  private const string EfCorePrefix = "Microsoft.EntityFrameworkCore";

  /// <summary>
  /// Returns true if the deps.json at <paramref name="depsFilePath"/> references EF Core
  /// (any library whose name starts with "Microsoft.EntityFrameworkCore"). Returns false when
  /// the file is missing or unreadable — auto-detection silently declines rather than failing
  /// the run.
  /// </summary>
  public static bool ReferencesEfCore(string? depsFilePath)
  {
    if (string.IsNullOrEmpty(depsFilePath) || !File.Exists(depsFilePath)) return false;
    try
    {
      using var stream = File.OpenRead(depsFilePath);
      using var reader = new DependencyContextJsonReader();
      var context = reader.Read(stream);
      foreach (var lib in context.RuntimeLibraries)
      {
        if (lib.Name.StartsWith(EfCorePrefix, StringComparison.Ordinal)) return true;
      }
      foreach (var lib in context.CompileLibraries)
      {
        if (lib.Name.StartsWith(EfCorePrefix, StringComparison.Ordinal)) return true;
      }
    }
    catch { /* silently decline on any parse / IO failure */ }
    return false;
  }
}
