using System.Reflection;
using System.Runtime.InteropServices;

namespace EasyDotnet.Infrastructure.Services;

public static class NetCoreDbgLocator
{
  /// <summary>
  /// Returns the full path to the netcoredbg executable for the current platform.
  /// </summary>
  public static string GetNetCoreDbgPath()
  {
    var netcoredbgDir = GetNetCoreDbgBaseDir();
    var platform = GetRuntimePlatform();
    var platformDir = Path.Combine(netcoredbgDir, platform);

    var executableName = OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg";
    var netcoredbgPath = Path.Combine(platformDir, executableName);

    if (!File.Exists(netcoredbgPath))
    {
      throw new FileNotFoundException(
        $"netcoredbg executable not found for platform '{platform}'",
        netcoredbgPath);
    }

    return netcoredbgPath;
  }

  /// <summary>
  /// Returns the base netcoredbg folder inside the .NET tool installation folder.
  /// </summary>
  private static string GetNetCoreDbgBaseDir()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    var toolExeDir = Path.GetDirectoryName(assemblyLocation)
                     ?? throw new InvalidOperationException("Unable to determine assembly directory");

    var storeToolsDir = Path.Combine(toolExeDir, "..", "..", "..", "tools");
    return Path.GetFullPath(Path.Combine(storeToolsDir, "netcoredbg"));
  }

  /// <summary>
  /// Determines the current runtime platform identifier matching the bundled folder structure.
  /// </summary>
  private static string GetRuntimePlatform()
  {
    if (OperatingSystem.IsWindows())
      return "win-x64";

    if (OperatingSystem.IsLinux())
    {
      return RuntimeInformation.ProcessArchitecture switch
      {
        Architecture.Arm64 => "linux-arm64",
        Architecture.X64 => "linux-x64",
        _ => throw new PlatformNotSupportedException(
          $"Linux architecture '{RuntimeInformation.ProcessArchitecture}' is not supported")
      };
    }

    if (OperatingSystem.IsMacOS())
    {
      return "osx-x64"; // netcoredbg only ships x64 for macOS
    }

    throw new PlatformNotSupportedException(
      $"Operating system '{RuntimeInformation.OSDescription}' is not supported");
  }
}