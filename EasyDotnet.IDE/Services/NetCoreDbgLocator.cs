using System.Reflection;
using System.Runtime.InteropServices;

namespace EasyDotnet.IDE.Services;

public enum DebuggerEngine
{
  NetCoreDbg,
  DncDbg,
  SharpDbg,
  Custom
}

public sealed record DebuggerResolution(
    DebuggerEngine Engine,
    string Source,
    string? Platform,
    string Path,
    string FileName,
    IReadOnlyList<string> Arguments);

public static class DebuggerLocator
{
  public static DebuggerResolution ResolveDebugger(string? engineName = null, string? debuggerBinPath = null)
  {
    var customPath = !string.IsNullOrWhiteSpace(debuggerBinPath)
        ? debuggerBinPath
        : WellKnownEnvironment.DebuggerBinPath.Value;
    var engine = GetConfiguredEngine(engineName, customPath);

    if (!string.IsNullOrWhiteSpace(customPath))
    {
      if (File.Exists(customPath))
      {
        var (customFileName, customArgs) = GetLaunchCommand(engine, customPath);
        return new DebuggerResolution(
            engine,
            !string.IsNullOrWhiteSpace(debuggerBinPath) ? "--debugger-bin-path" : WellKnownEnvironment.DebuggerBinPath.Name,
            TryGetRuntimePlatform(),
            customPath,
            customFileName,
            customArgs);
      }

      throw new FileNotFoundException(
          $"Custom debugger executable specified in {(!string.IsNullOrWhiteSpace(debuggerBinPath) ? "--debugger-bin-path" : WellKnownEnvironment.DebuggerBinPath.Name)} not found",
          customPath);
    }

    var platform = GetRuntimePlatform();
    var debuggerPath = GetBundledDebuggerPath(engine, platform);

    if (!File.Exists(debuggerPath))
    {
      throw new FileNotFoundException(
          $"{DebuggerEngineFactory.Get(engine).Name} executable not found for platform '{platform}'",
          debuggerPath);
    }

    var (fileName, arguments) = GetLaunchCommand(engine, debuggerPath);
    return new DebuggerResolution(engine, "bundled", platform, debuggerPath, fileName, arguments);
  }

  public static (string FileName, IReadOnlyList<string> Arguments) GetLaunchCommand(DebuggerEngine engine, string debuggerPath) =>
    DebuggerEngineFactory.Get(engine).BuildLaunchCommand(debuggerPath);

  public static (string FileName, IReadOnlyList<string> Arguments) GetVersionCommand(DebuggerEngine engine, string debuggerPath) =>
    DebuggerEngineFactory.Get(engine).BuildVersionCommand(debuggerPath);

  public static string GetDebuggerPath(string? engineName = null) => ResolveDebugger(engineName).Path;

  public static DebuggerEngine GetConfiguredEngine(string? engineName = null, string? debuggerBinPath = null)
  {
    var customPath = !string.IsNullOrWhiteSpace(debuggerBinPath)
        ? debuggerBinPath
        : WellKnownEnvironment.DebuggerBinPath.Value;

    if (!string.IsNullOrWhiteSpace(customPath))
      return DebuggerEngine.Custom;

    var configuredEngine = !string.IsNullOrWhiteSpace(engineName)
        ? engineName
        : WellKnownEnvironment.DebuggerEngine.Value;

    return string.IsNullOrWhiteSpace(configuredEngine)
        ? DebuggerEngine.NetCoreDbg
        : ParseEngine(configuredEngine);
  }

  public static DebuggerEngine ParseEngine(string engineName) => DebuggerEngineFactory.Parse(engineName).Engine;

  public static string GetBundledDebuggerPath(DebuggerEngine engine, string platform) =>
    Path.Combine(GetToolsBaseDir(), DebuggerEngineFactory.Get(engine).GetBundledRelativePath(platform));

  public static string GetRuntimePlatform()
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
      return RuntimeInformation.ProcessArchitecture switch
      {
        Architecture.Arm64 => "osx-arm64",
        Architecture.X64 => "osx-x64",
        _ => throw new PlatformNotSupportedException(
            $"macOS architecture '{RuntimeInformation.ProcessArchitecture}' is not supported")
      };
    }

    throw new PlatformNotSupportedException(
      $"Operating system '{RuntimeInformation.OSDescription}' is not supported");
  }

  public static string? TryGetRuntimePlatform()
  {
    try
    {
      return GetRuntimePlatform();
    }
    catch
    {
      return null;
    }
  }

  public static string GetEngineName(DebuggerEngine engine) => DebuggerEngineFactory.Get(engine).Name;

  private static string GetToolsBaseDir()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    var toolExeDir = Path.GetDirectoryName(assemblyLocation)
                     ?? throw new InvalidOperationException("Unable to determine assembly directory");

    var storeToolsDir = Path.Combine(toolExeDir, "..", "..", "..", "tools");
    return Path.GetFullPath(storeToolsDir);
  }
}

public static class NetCoreDbgLocator
{

  /// <summary>
  /// Returns the full path to the netcoredbg executable for the current platform.
  /// </summary>
  public static string GetNetCoreDbgPath() => DebuggerLocator.ResolveDebugger("netcoredbg").Path;

  public static string GetBundledNetCoreDbgPath(string platform) =>
    DebuggerLocator.GetBundledDebuggerPath(DebuggerEngine.NetCoreDbg, platform);

  /// <summary>
  /// Determines the current runtime platform identifier matching the bundled folder structure.
  /// </summary>
  public static string GetRuntimePlatform() => DebuggerLocator.GetRuntimePlatform();
}