using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace EasyDotnet.IDE.Logging;

/// <summary>
/// Mutable logging state shared across the IDE process.
/// `Off` is mapped to `Error` so unhandled exceptions still surface even when logging is
/// disabled — matching the "vim.lsp.log_level off still reports crashes" expectation.
/// </summary>
public sealed class LogLevelState
{
  public LoggingLevelSwitch Switch { get; } = new();
  public InMemoryRingSink RingSink { get; }

  private SourceLevels _current;
  public event Action<SourceLevels>? LevelChanged;

  public LogLevelState(SourceLevels initial, int ringCapacity = 5000)
  {
    RingSink = new InMemoryRingSink(ringCapacity);
    Set(initial);
  }

  public SourceLevels Current => _current;

  public void Set(SourceLevels level)
  {
    _current = level;
    Switch.MinimumLevel = ToSerilog(level);
    LevelChanged?.Invoke(level);
  }

  public static LogEventLevel ToSerilog(SourceLevels level) => level switch
  {
    SourceLevels.Off => LogEventLevel.Error,
    SourceLevels.Critical => LogEventLevel.Fatal,
    SourceLevels.Error => LogEventLevel.Error,
    SourceLevels.Warning => LogEventLevel.Warning,
    SourceLevels.Information => LogEventLevel.Information,
    SourceLevels.Verbose => LogEventLevel.Verbose,
    SourceLevels.All => LogEventLevel.Verbose,
    _ => LogEventLevel.Information,
  };

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
