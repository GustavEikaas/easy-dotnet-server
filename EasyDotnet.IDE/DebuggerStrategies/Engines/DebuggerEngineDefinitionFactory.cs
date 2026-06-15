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
    return new SharpDbgEngineDefinition("dotnet", dllPath);
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
}