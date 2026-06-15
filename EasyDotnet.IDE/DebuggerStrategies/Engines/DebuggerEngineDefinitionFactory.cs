using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.DebuggerStrategies.Engines;

public interface IDebuggerEngineDefinitionFactory
{
  IDebuggerEngineDefinition Create(DebuggerOptions? options);
}

public sealed class DebuggerEngineDefinitionFactory : IDebuggerEngineDefinitionFactory
{
  public IDebuggerEngineDefinition Create(DebuggerOptions? options)
  {
    var resolution = DebuggerLocator.ResolveDebugger(
      engineName: options?.Engine,
      debuggerBinPath: options?.BinaryPath);

    return resolution.Engine switch
    {
      DebuggerEngine.NetCoreDbg => new NetCoreDbgEngineDefinition(resolution.Path),
      DebuggerEngine.DncDbg => new DncDbgEngineDefinition(resolution.Path),
      DebuggerEngine.SharpDbg => BuildSharpDbg(resolution.Path),
      DebuggerEngine.Custom => BuildCustom(resolution.Path, options),
      _ => throw new ArgumentOutOfRangeException(nameof(resolution.Engine), resolution.Engine, null)
    };
  }

  private static SharpDbgEngineDefinition BuildSharpDbg(string dllPath)
  {
    // SharpDbg is a managed DLL — launch it via the dotnet host on PATH.
    var dotnetPath = ResolveDotnetHost();
    return new SharpDbgEngineDefinition(dotnetPath, dllPath);
  }

  private static CustomBinaryEngineDefinition BuildCustom(string binaryPath, DebuggerOptions? options)
  {
    var args = ResolveCustomArgs(options);
    return new CustomBinaryEngineDefinition(binaryPath, args);
  }

  private static IReadOnlyList<string>? ResolveCustomArgs(DebuggerOptions? options)
  {
    // Explicit args from DebuggerOptions take precedence over the environment variable.
    if (options?.BinaryArgs is { Length: > 0 } optionArgs)
      return optionArgs;

    var envArgs = Environment.GetEnvironmentVariable(DebuggerLocator.DEBUGGER_BIN_ARGS_ENV);
    if (!string.IsNullOrWhiteSpace(envArgs))
      return envArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    return null;
  }

  private static string ResolveDotnetHost()
  {
    // Prefer the dotnet host that launched this process (guaranteed present),
    // falling back to the name on PATH.
    var processPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
    if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
      return processPath;

    return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
  }
}