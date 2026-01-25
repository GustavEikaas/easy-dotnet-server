using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EasyDotnet.TestRunner.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum TestAction
{
  Run,
  Debug,
  PeekOutput,
  GoToSource,
  Refresh
}

public abstract record TestNodeStatus
{
  public List<TestAction> Actions { get; init; } = [];
  public string Type => GetType().Name;

  public sealed record Idle : TestNodeStatus;
  public sealed record Queued : TestNodeStatus;
  public sealed record Building : TestNodeStatus;
  public sealed record Discovering : TestNodeStatus;
  public sealed record Running : TestNodeStatus;
  public sealed record Debugging : TestNodeStatus;
  public sealed record Cancelling : TestNodeStatus;
  public sealed record Cancelled : TestNodeStatus;

  public sealed record Passed(string DurationDisplay) : TestNodeStatus;
  public sealed record Failed(string DurationDisplay, string ErrorMessage) : TestNodeStatus;
  public sealed record Skipped(string Reason) : TestNodeStatus;
}