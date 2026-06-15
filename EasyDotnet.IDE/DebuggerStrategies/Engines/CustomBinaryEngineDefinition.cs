using EasyDotnet.Debugger;

namespace EasyDotnet.IDE.DebuggerStrategies.Engines;

/// <summary>
/// Engine definition for a user-supplied debugger binary. All polyfill features are disabled
/// by default because the capabilities of an arbitrary debugger are unknown.
/// </summary>
public sealed class CustomBinaryEngineDefinition(
  string binaryPath,
  IReadOnlyList<string>? processArguments = null,
  IReadOnlyDictionary<string, string>? environmentVariables = null) : IDebuggerEngineDefinition
{
  public string BinaryPath { get; } = binaryPath;
  public IReadOnlyList<string> ProcessArguments { get; } = processArguments ?? [];
  public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; } = environmentVariables;
  public DebuggerProxyFeatures Features { get; } = DebuggerProxyFeatures.None;
}