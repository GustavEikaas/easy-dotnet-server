using Newtonsoft.Json;

namespace EasyDotnet.TestRunner.Models;

[JsonConverter(typeof(UnionTypeNameConverter<TestNodeStatus>))]
public abstract record TestNodeStatus
{
  public sealed record Idle : TestNodeStatus;
  public sealed record Queued : TestNodeStatus;
  public sealed record Building : TestNodeStatus;
  public sealed record Discovering : TestNodeStatus;
  public sealed record Running : TestNodeStatus;
  public sealed record Debugging : TestNodeStatus;
  public sealed record Cancelling : TestNodeStatus;
  public sealed record Passed : TestNodeStatus;
  public sealed record Failed : TestNodeStatus;
  public sealed record Skipped : TestNodeStatus;
  public sealed record Cancelled : TestNodeStatus;
}