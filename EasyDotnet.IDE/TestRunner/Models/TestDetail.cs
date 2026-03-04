using EasyDotnet.IDE.TestRunner.Adapters;

namespace EasyDotnet.IDE.TestRunner.Models;

/// <summary>
/// Cached per-node result detail. Written by adapters after a run.
/// Never pushed to the client — fetched lazily via testrunner/getResults.
/// Cleared when a new operation begins on the node.
/// </summary>
public record TestDetail(
    string[] ErrorMessage,
    long? DurationMs,
    ParsedStackFrame[] Frames,
    ParsedStackFrame? FailingFrame,
    string[] Stdout
)
{
  public bool HasResults => Frames.Length > 0 || Stdout.Length > 0 || ErrorMessage.Length > 0;
}