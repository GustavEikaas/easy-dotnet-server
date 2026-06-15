using EasyDotnet.Debugger;

namespace EasyDotnet.IDE.DebuggerStrategies.Engines;

public sealed class NetCoreDbgEngineDefinition(string binaryPath) : IDebuggerEngineDefinition
{
  public string BinaryPath { get; } = binaryPath;
  public IReadOnlyList<string> ProcessArguments { get; } = ["--interpreter=vscode"];
  public IReadOnlyDictionary<string, string>? EnvironmentVariables => null;
  public DebuggerProxyFeatures Features { get; } = DebuggerProxyFeatures.NetCoreDbgDefaults;
}