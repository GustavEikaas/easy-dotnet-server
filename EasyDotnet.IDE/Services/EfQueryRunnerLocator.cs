using System.Reflection;

namespace EasyDotnet.IDE.Services;

public static class EfQueryRunnerLocator
{
  public static string GetPath()
  {
#if DEBUG
    var path = Path.GetFullPath(Path.Join(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        "../../../../EasyDotnet.EfQueryRunner/bin/Debug/net8.0/EasyDotnet.EfQueryRunner.dll"));
#else
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to determine assembly directory.");
        var path = Path.Combine(assemblyDir, "..", "..", "..", "tools", "EfQueryRunner", "net8.0", "EasyDotnet.EfQueryRunner.dll");
#endif
    if (!File.Exists(path))
    {
      throw new FileNotFoundException("EfQueryRunner dll not found.", path);
    }

    return path;
  }
}