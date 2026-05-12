using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public readonly record struct PropertyCacheKey(
    string ProjectFullPath,
    string Configuration,
    string TargetFramework,
    string MsBuildVersion)
{
  public static PropertyCacheKey Create(string projectPath, string configuration, string targetFramework, string msBuildVersion)
  {
    var full = Path.GetFullPath(projectPath);
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      full = full.ToLowerInvariant();
    }
    return new PropertyCacheKey(full, configuration, targetFramework, msBuildVersion);
  }

  public string ToDiskFileName()
  {
    var raw = $"{ProjectFullPath}|{Configuration}|{TargetFramework}|{MsBuildVersion}";
    return Sha256Hex(raw).Substring(0, 16) + ".props.json";
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
