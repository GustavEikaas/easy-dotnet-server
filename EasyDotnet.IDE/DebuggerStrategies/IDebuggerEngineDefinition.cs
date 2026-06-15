using EasyDotnet.Debugger;

namespace EasyDotnet.IDE.DebuggerStrategies;

public interface IDebuggerEngineDefinition
{
  /// <summary>The path to the debugger executable (or the host runtime, e.g. <c>dotnet</c>).</summary>
  string BinaryPath { get; }

  /// <summary>
  /// Arguments passed to the debugger process. For managed debuggers (e.g. SharpDbg) this
  /// includes the DLL path as the first element followed by debugger-specific flags.
  /// </summary>
  IReadOnlyList<string> ProcessArguments { get; }

  /// <summary>Optional extra environment variables to set on the debugger process.</summary>
  IReadOnlyDictionary<string, string>? EnvironmentVariables { get; }

  /// <summary>Polyfill feature flags — controls which additional proxy interceptions are active.</summary>
  DebuggerProxyFeatures Features { get; }
}