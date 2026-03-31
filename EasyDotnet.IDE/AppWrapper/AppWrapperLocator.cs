using System.Reflection;

namespace EasyDotnet.IDE.AppWrapper;

public static class AppWrapperLocator
{
  public static string GetPath()
  {
#if DEBUG
    var path = Path.GetFullPath(Path.Join(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        "../../../../EasyDotnet.AppWrapper/bin/Debug/net8.0/EasyDotnet.AppWrapper.dll"));
#else
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to determine assembly directory.");
        var path = Path.Combine(assemblyDir,"..","..","..","tools", "AppWrapper", "net8.0", "EasyDotnet.AppWrapper.dll");
#endif
    if (!File.Exists(path))
    {
      throw new FileNotFoundException("AppWrapper dll not found.", path);
    }

    return path;
  }
}