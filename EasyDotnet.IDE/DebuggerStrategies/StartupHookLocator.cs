using System.Reflection;

namespace EasyDotnet.IDE.DebuggerStrategies;

public static class StartupHookLocator
{
  public static string GetStartupHookPath()
  {
    var path = "";
#if DEBUG
    path = Path.GetFullPath(Path.Join(GetAssemblyDir(), "../../../../EasyDotnet.StartupHook/bin/Debug/net6.0/EasyDotnet.StartupHook.dll"));
#else
    path = Path.Combine(GetBaseDir(), "StartupHook", "EasyDotnet.StartupHook.dll");
#endif
    if (!File.Exists(path))
    {
      throw new Exception("StartupHook dll not found");
    }
    return path;
  }

  private static string GetAssemblyDir()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    return Path.GetDirectoryName(assemblyLocation)
        ?? throw new InvalidOperationException("Unable to determine assembly directory");
  }

  private static string GetBaseDir()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    var toolExeDir = Path.GetDirectoryName(assemblyLocation) ?? throw new InvalidOperationException("Unable to determine assembly directory");
    return Path.Combine(toolExeDir, "DebuggerPayloads");
  }
}
