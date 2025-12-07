namespace EasyDotnet.Debugger;

public class DebugOutputEvent
{
  /// <summary>
  /// The output text
  /// </summary>
  public required string Output { get; init; }

  /// <summary>
  /// Category of output: stdout, stderr, console, telemetry, etc.
  /// </summary>
  public required string Category { get; init; }

  /// <summary>
  /// Optional source information
  /// </summary>
  public DebugOutputSource? Source { get; init; }

  /// <summary>
  /// Optional line number in the source
  /// </summary>
  public int? Line { get; init; }

  /// <summary>
  /// Optional column number
  /// </summary>
  public int? Column { get; init; }

  /// <summary>
  /// Optional data associated with this output
  /// </summary>
  public object? Data { get; init; }

  /// <summary>
  /// Optional group identifier for related output
  /// </summary>
  public string? Group { get; init; }

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
