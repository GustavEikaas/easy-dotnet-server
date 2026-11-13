using System.IO.Abstractions;

namespace EasyDotnet.IDE.TestRunner.Store;

/// <summary>
/// Detects possibly stale discovery: the test DLL's write time and size, plus the newest write time
/// and count of the top-level DLLs next to it (a rebuilt dependency). Other files are ignored
/// because test runs write logs there.
/// </summary>
public readonly record struct AssemblyStamp(
    DateTime LastWriteTimeUtc,
    long Length,
    DateTime NewestAssemblyWriteTimeUtc,
    int AssemblyCount)
{
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