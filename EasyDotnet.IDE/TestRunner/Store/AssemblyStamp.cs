using System.IO.Abstractions;

namespace EasyDotnet.IDE.TestRunner.Store;

/// <summary>
/// Last-write time + size of a compiled test assembly — a cheap proxy for "the DLL was rebuilt".
/// A no-op build leaves both untouched, so equal stamps mean previously discovered tests are still valid.
/// </summary>
public readonly record struct AssemblyStamp(DateTime LastWriteTimeUtc, long Length)
{
  /// <summary>Returns null when the file is missing or unreadable — callers treat that as "changed".</summary>
  public static AssemblyStamp? TryCapture(IFileSystem fileSystem, string? path)
  {
    if (string.IsNullOrWhiteSpace(path)) return null;

    try
    {
      var info = fileSystem.FileInfo.New(path);
      return info.Exists ? new AssemblyStamp(info.LastWriteTimeUtc, info.Length) : null;
    }
    catch (Exception)
    {
      return null;
    }
  }
}
