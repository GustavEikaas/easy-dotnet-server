using EasyDotnet.Debugger;

namespace EasyDotnet.IDE.DebuggerStrategies.Engines;

public sealed class SharpDbgEngineDefinition(string dotnetPath, string sharpDbgDllPath) : IDebuggerEngineDefinition
{
  public string BinaryPath { get; } = dotnetPath;

  // First argument is the managed DLL path; subsequent args are debugger flags.
  public IReadOnlyList<string> ProcessArguments { get; } = [sharpDbgDllPath, "--interpreter=vscode"];

  public IReadOnlyDictionary<string, string>? EnvironmentVariables => null;
  public DebuggerProxyFeatures Features { get; } = DebuggerProxyFeatures.SharpDbgDefaults;
}