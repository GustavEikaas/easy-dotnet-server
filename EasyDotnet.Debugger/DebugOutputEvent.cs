namespace EasyDotnet.Debugger;

public class DebugOutputEvent
{
  /// <summary>
  /// The output text
  /// </summary>
  public required string[] Output { get; init; }

  /// <summary>
  /// Category of output: stdout, stderr, console, telemetry, etc.
  /// </summary>
  public required string Category { get; init; }

  /// <summary>
  /// Optional source information
  /// </summary>
  public DebugOutputSource? Source { get; init; }

  /// <summary>
  /// Timestamp when output was received
  /// </summary>
  public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public class DebugOutputSource
{
  public string? Name { get; init; }
  public string? Path { get; init; }
  public int? SourceReference { get; init; }
}
