using System.Reflection;
using System.Runtime.InteropServices;

namespace EasyDotnet.IDE.Services;

public enum DebuggerEngine
{
  NetCoreDbg,
  DncDbg
}

public sealed record DebuggerResolution(DebuggerEngine Engine, string Source, string? Platform, string Path);

public static class DebuggerLocator
{
  public const string DEBUGGER_PATH_ENV = "EASY_DOTNET_DEBUGGER_BIN_PATH";
  public const string DEBUGGER_ENGINE_ENV = "EASY_DOTNET_DEBUGGER_ENGINE";

  public static DebuggerResolution ResolveDebugger(string? engineName = null, string? debuggerBinPath = null)
  {
    var customPath = !string.IsNullOrWhiteSpace(debuggerBinPath)
        ? debuggerBinPath
        : Environment.GetEnvironmentVariable(DEBUGGER_PATH_ENV);
    var engine = GetConfiguredEngine(engineName);

    if (!string.IsNullOrWhiteSpace(customPath))
    {
      if (File.Exists(customPath))
      {
        return new DebuggerResolution(
            engine,
            !string.IsNullOrWhiteSpace(debuggerBinPath) ? "--debugger-bin-path" : DEBUGGER_PATH_ENV,
            TryGetRuntimePlatform(),
            customPath);
      }

      throw new FileNotFoundException(
          $"Custom debugger executable specified in {(!string.IsNullOrWhiteSpace(debuggerBinPath) ? "--debugger-bin-path" : DEBUGGER_PATH_ENV)} not found",
          customPath);
    }

    var platform = GetRuntimePlatform();
    var debuggerPath = GetBundledDebuggerPath(engine, platform);

    if (!File.Exists(debuggerPath))
    {
      throw new FileNotFoundException(
          $"{GetEngineExecutableName(engine)} executable not found for platform '{platform}'",
          debuggerPath);
    }

    return new DebuggerResolution(engine, "bundled", platform, debuggerPath);
  }

  public static string GetDebuggerPath(string? engineName = null) => ResolveDebugger(engineName).Path;

  public static DebuggerEngine GetConfiguredEngine(string? engineName = null)
  {
    var configuredEngine = !string.IsNullOrWhiteSpace(engineName)
        ? engineName
        : Environment.GetEnvironmentVariable(DEBUGGER_ENGINE_ENV);

    return string.IsNullOrWhiteSpace(configuredEngine)
        ? DebuggerEngine.NetCoreDbg
        : ParseEngine(configuredEngine);
  }

  public static DebuggerEngine ParseEngine(string engineName) =>
    engineName.Trim().ToLowerInvariant() switch
    {
      "netcoredbg" or "netcore" => DebuggerEngine.NetCoreDbg,
      "dncdbg" or "dnc" => DebuggerEngine.DncDbg,
      _ => throw new ArgumentException(
          $"Unsupported debugger engine '{engineName}'. Supported values are 'netcoredbg' and 'dncdbg'.",
          nameof(engineName))
    };

  public static string GetBundledDebuggerPath(DebuggerEngine engine, string platform)
  {
    var debuggerDir = Path.Combine(GetToolsBaseDir(), GetEngineDirectoryName(engine));
    var platformDir = Path.Combine(debuggerDir, platform);
    return Path.Combine(platformDir, GetEngineExecutableName(engine));
  }

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

  public static string GetEngineName(DebuggerEngine engine) =>
    engine switch
    {
      DebuggerEngine.NetCoreDbg => "netcoredbg",
      DebuggerEngine.DncDbg => "dncdbg",
      _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

  private static string GetEngineDirectoryName(DebuggerEngine engine) => GetEngineName(engine);

  private static string GetEngineExecutableName(DebuggerEngine engine) =>
    engine switch
    {
      DebuggerEngine.NetCoreDbg => OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg",
      DebuggerEngine.DncDbg => OperatingSystem.IsWindows() ? "dncdbg.exe" : "dncdbg",
      _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

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
  public const string DEBUGGER_PATH_ENV = DebuggerLocator.DEBUGGER_PATH_ENV;

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