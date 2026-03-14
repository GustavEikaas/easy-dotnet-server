using Newtonsoft.Json;

namespace EasyDotnet.IDE.TestRunner.Models;

public abstract record TestNodeStatus
{
  public string Type => GetType().Name;

  [JsonIgnore]
  public abstract TestNodeStatusKind Kind { get; }

  public sealed record Idle : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Idle;
  }

  public sealed record Queued : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Queued;
  }

  public sealed record Building : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Building;
  }

  public sealed record Discovering : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Discovering;
  }

  public sealed record Running : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Running;
  }

  public sealed record Debugging : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Debugging;
  }

  public sealed record Cancelling : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Cancelling;
  }

  public sealed record Cancelled : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Cancelled;
  }

  public sealed record Passed(string DurationDisplay) : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Passed;
  }

  public sealed record Failed(string DurationDisplay, string[] ErrorMessage) : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Failed;
  }

  public sealed record BuildFailed : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.BuildFailed;
  }

  public sealed record Skipped(string Reason) : TestNodeStatus
  {
    public override TestNodeStatusKind Kind => TestNodeStatusKind.Skipped;
  }
}