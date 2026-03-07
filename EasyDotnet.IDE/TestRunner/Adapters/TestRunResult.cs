using System.Text.RegularExpressions;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// Normalised result from either VSTest or MTP.
/// All async streams from the underlying SDKs are materialised before this
/// record is created — no IAsyncEnumerable here.
/// </summary>
public sealed record TestRunResult
{
  public required string NativeId { get; init; }
  public required string Outcome { get; init; }   // "passed" | "failed" | "skipped" | "none"
  public required long? DurationMs { get; init; }
  public required string[] ErrorMessage { get; init; }
  public required string[] Stdout { get; init; }
  public required ParsedStackFrame[] Frames { get; init; }
  public ParsedStackFrame? FailingFrame { get; init; }
}

public sealed record ParsedStackFrame
{
  public required string OriginalText { get; init; }
  public string? File { get; init; }
  public int? Line { get; init; }
  public bool IsUserCode => !string.IsNullOrEmpty(File) && System.IO.File.Exists(File);
}

public static partial class SimpleStackTraceParser
{
  private static readonly Regex FileLineRegex = StackTraceLineRegex();

  public static ParsedStackFrame[] Parse(string? stackTrace)
  {
    if (string.IsNullOrWhiteSpace(stackTrace)) return [];

    var lines = stackTrace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    var frames = new ParsedStackFrame[lines.Length];

    for (var i = 0; i < lines.Length; i++)
    {
      var originalLine = lines[i].Trim();
      var match = FileLineRegex.Match(originalLine);

      if (match.Success)
      {
        frames[i] = new ParsedStackFrame
        {
          OriginalText = originalLine,
          File = match.Groups["file"].Value.Trim(),
          Line = int.Parse(match.Groups["line"].Value)
        };
      }
      else
      {
        frames[i] = new ParsedStackFrame
        {
          OriginalText = originalLine,
          File = null,
          Line = null
        };
      }
    }

    return frames;
  }

  [GeneratedRegex(@"\s+in\s+(?<file>.*?):line\s+(?<line>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
  private static partial Regex StackTraceLineRegex();
}