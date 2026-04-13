using System.Text.Json;

namespace EasyDotnet.IDE.BuildHost;

public static class GlobalJsonReader
{
  /// <summary>
  /// Walks up from <paramref name="startDir"/> looking for a global.json with an sdk.version
  /// field and returns the .NET major version it requires (e.g. 8 for "8.0.415").
  /// Returns false if no global.json is found or the file has no sdk.version.
  /// </summary>
  public static bool TryReadSdkMajorVersion(string startDir, out int major)
  {
    major = 0;

    var dir = startDir;
    while (!string.IsNullOrEmpty(dir))
    {
      var candidate = Path.Combine(dir, "global.json");
      if (File.Exists(candidate) && TryParseMajorFromGlobalJson(candidate, out major))
        return true;

      var parent = Path.GetDirectoryName(dir);
      if (parent == dir) break;
      dir = parent!;
    }

    return false;
  }

  private static bool TryParseMajorFromGlobalJson(string path, out int major)
  {
    major = 0;
    try
    {
      using var stream = File.OpenRead(path);
      using var doc = JsonDocument.Parse(stream);

      if (!doc.RootElement.TryGetProperty("sdk", out var sdk)) return false;
      if (!sdk.TryGetProperty("version", out var version)) return false;

      var versionStr = version.GetString();
      if (string.IsNullOrEmpty(versionStr)) return false;

      var dotIndex = versionStr.IndexOf('.');
      if (dotIndex < 0) return false;

      return int.TryParse(versionStr[..dotIndex], out major) && major > 0;
    }
    catch
    {
      return false;
    }
  }
}