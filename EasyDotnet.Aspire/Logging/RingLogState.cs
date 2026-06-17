using Microsoft.Extensions.Logging;

namespace EasyDotnet.Aspire.Logging;

public sealed class RingLogState(LogLevel initial)
{
  private volatile int _minLevel = (int)initial;

  public InMemoryRingSink Sink { get; } = new(5000);

  public LogLevel MinLevel => (LogLevel)_minLevel;
  public bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= MinLevel;
  public void SetLevel(LogLevel level) => _minLevel = (int)level;

  public static LogLevel Parse(string value) => value.Trim().ToLowerInvariant() switch
  {
    "off" or "none" => LogLevel.None,
    "critical" or "fatal" => LogLevel.Critical,
    "error" => LogLevel.Error,
    "warning" or "warn" => LogLevel.Warning,
    "information" or "info" => LogLevel.Information,
    "debug" => LogLevel.Debug,
    "verbose" or "trace" or "all" => LogLevel.Trace,
    _ => LogLevel.Information,
  };
}