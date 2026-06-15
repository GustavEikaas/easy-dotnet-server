using EasyDotnet.Debugger;

namespace EasyDotnet.IDE.DebuggerStrategies.Engines;

public sealed class SharpDbgEngineDefinition(string sharpDbgDllPath) : IDebuggerEngineDefinition
{
  public string BinaryPath { get; } = "dotnet";

  public IReadOnlyList<string> ProcessArguments { get; } = [sharpDbgDllPath, "--interpreter=vscode"];

  public IReadOnlyDictionary<string, string>? EnvironmentVariables => null;

  public DebuggerProxyFeatures Features { get; } = DebuggerProxyFeatures.SharpDbgDefaults;
}