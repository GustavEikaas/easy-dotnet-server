namespace EasyDotnet.IDE.TestRunner.Models;

public enum TestNodeStatusKind
{
  Idle,
  Queued,
  Building,
  Discovering,
  Running,
  Debugging,
  Cancelling,
  Cancelled,
  Passed,
  Failed,
  BuildFailed,
  Skipped
}

public static class TestNodeStatusKindExtensions
{
  public static bool IsTransient(this TestNodeStatusKind kind) => kind is
      TestNodeStatusKind.Queued or
      TestNodeStatusKind.Building or
      TestNodeStatusKind.Discovering or
      TestNodeStatusKind.Running or
      TestNodeStatusKind.Debugging or
      TestNodeStatusKind.Cancelling;

  public static bool IsCancellable(this TestNodeStatusKind kind) => kind is
      TestNodeStatusKind.Queued or
      TestNodeStatusKind.Running or
      TestNodeStatusKind.Debugging or
      TestNodeStatusKind.Cancelling;
}