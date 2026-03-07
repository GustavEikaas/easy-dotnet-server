namespace EasyDotnet.IDE.TestRunner.Executor;

public class RunProgressCounter(int totalTests)
{
  private int _passed;
  private int _failed;
  private int _skipped;
  private int _cancelled;
  private int _running = totalTests;

  public int TotalTests { get; } = totalTests;

  public void Record(string outcome)
  {
    Interlocked.Decrement(ref _running);
    switch (outcome)
    {
      case "passed": Interlocked.Increment(ref _passed); break;
      case "failed": Interlocked.Increment(ref _failed); break;
      case "skipped": Interlocked.Increment(ref _skipped); break;
      case "cancelled": Interlocked.Increment(ref _cancelled); break;
    }
  }

  public (int Running, int Passed, int Failed, int Skipped, int Cancelled) Snapshot() =>
      (Math.Max(0, _running), _passed, _failed, _skipped, _cancelled);
}