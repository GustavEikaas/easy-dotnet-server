using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public readonly record struct PropertyCacheKey(
    string ProjectFullPath,
    string Configuration,
    string Platform,
    string TargetFramework,
    string MsBuildVersion)
{
  public static PropertyCacheKey Create(string projectPath, string configuration, string? platform, string targetFramework, string msBuildVersion)
  {
    var full = Path.GetFullPath(projectPath);
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      full = full.ToLowerInvariant();
    }
    return new PropertyCacheKey(full, configuration, platform ?? string.Empty, targetFramework, msBuildVersion);
  }

  public string ToDiskFileName()
  {
    var raw = $"{ProjectFullPath}|{Configuration}|{Platform}|{TargetFramework}|{MsBuildVersion}";
    return Sha256Hex(raw)[..16] + ".props.json";
  }

  private static string Sha256Hex(string input)
  {
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
    var sb = new StringBuilder(bytes.Length * 2);
    foreach (var b in bytes)
    {
      sb.Append(b.ToString("x2"));
    }
    return sb.ToString();
  }
}