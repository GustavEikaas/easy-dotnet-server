using System.IO.Abstractions;

namespace EasyDotnet.IDE.TestRunner.Store;

/// <summary>
/// Cheap proxy for "the discovered tests may have changed": last-write time + size of the test
/// assembly, plus the newest last-write time and count of the assemblies next to it. The latter
/// catches a referenced library rebuilt without its public API changing (the test DLL is then not
/// recompiled, yet inherited tests or theory data coming from that library can differ).
/// A no-op build leaves all of these untouched.
///
/// Only top-level *.dll files are considered: referenced assemblies are copied there, while
/// subdirectories hold satellite resources and native runtimes that don't affect discovery. Test runs
/// and build tooling write other files into the output directory (logs, coverlet mappings, …), which
/// would otherwise force a discovery on every run.
/// Non-assembly inputs to discovery (e.g. a JSON file feeding [MemberData]) are therefore not
/// tracked — invalidating the project picks those up.
/// </summary>
public readonly record struct AssemblyStamp(
    DateTime LastWriteTimeUtc,
    long Length,
    DateTime NewestAssemblyWriteTimeUtc,
    int AssemblyCount)
{
  /// <summary>Returns null when the file is missing or unreadable — callers treat that as "changed".</summary>
  public static AssemblyStamp? TryCapture(IFileSystem fileSystem, string? path)
  {
    if (string.IsNullOrWhiteSpace(path)) return null;

    try
    {
      var info = fileSystem.FileInfo.New(path);
      if (!info.Exists || info.Directory is not { } outputDir) return null;

      var (newest, count) = outputDir
          .EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly)
          .Aggregate(
              (Newest: info.LastWriteTimeUtc, Count: 0),
              (acc, f) => (f.LastWriteTimeUtc > acc.Newest ? f.LastWriteTimeUtc : acc.Newest, acc.Count + 1));

      return new AssemblyStamp(info.LastWriteTimeUtc, info.Length, newest, count);
    }
    catch (Exception)
    {
      return null;
    }
  }
}
