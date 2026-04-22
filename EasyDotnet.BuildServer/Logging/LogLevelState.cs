using System.Diagnostics;

namespace EasyDotnet.BuildServer.Logging;

public sealed class LogLevelState
{
  public InMemoryRingSink RingSink { get; }
  public event Action<SourceLevels>? LevelChanged;

  public LogLevelState(SourceLevels initial, int ringCapacity = 5000)
  {
    RingSink = new InMemoryRingSink(ringCapacity);
    Current = initial;
  }

  public SourceLevels Current { get; private set; }

  public void Set(SourceLevels level)
  {
    Current = level;
    LevelChanged?.Invoke(level);
  }

  public static SourceLevels Parse(string value) => value.ToLowerInvariant() switch
  {
    "off" => SourceLevels.Off,
    "critical" => SourceLevels.Critical,
    "error" => SourceLevels.Error,
    "warning" or "warn" => SourceLevels.Warning,
    "information" or "info" => SourceLevels.Information,
    "verbose" or "debug" or "trace" => SourceLevels.Verbose,
    "all" => SourceLevels.All,
    _ => throw new ArgumentException($"Unknown log level: {value}"),
  };
}
