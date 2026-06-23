namespace EasyDotnet.IDE.Services;

public static class DebuggerEngineFactory
{
  private static readonly IReadOnlyList<IDebuggerEngineDefinition> Definitions =
  [
    new NetCoreDbgDefinition(),
    new DncDbgDefinition(),
    new SharpDbgDefinition(),
    new CustomDebuggerEngineDefinition(),
  ];

  public static IDebuggerEngineDefinition Get(DebuggerEngine engine) =>
    Definitions.First(d => d.Engine == engine);

  public static IDebuggerEngineDefinition Parse(string engineName)
  {
    var normalized = engineName.Trim().ToLowerInvariant();
    return Definitions.FirstOrDefault(d => d.Name == normalized)
        ?? throw new ArgumentException(
            $"Unsupported debugger engine '{engineName}'. Supported values are {string.Join(", ", Definitions.Select(d => $"'{d.Name}'"))}.",
            nameof(engineName));
  }
}
