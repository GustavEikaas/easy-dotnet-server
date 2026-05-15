using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Executor;

public static class TestStatusPriority
{
  public static TestNodeStatus AggregateTerminalStatus(
      int passed,
      int failed,
      int skipped,
      int inconclusive,
      string durationDisplay)
  {
    if (failed > 0) return new TestNodeStatus.Failed(durationDisplay, []);
    if (passed > 0) return new TestNodeStatus.Passed(durationDisplay);
    if (skipped > 0) return new TestNodeStatus.Skipped("");
    if (inconclusive > 0) return new TestNodeStatus.Inconclusive("");
    return new TestNodeStatus.Passed(durationDisplay);
  }
}