using EasyDotnet.IDE.TestRunner.Executor;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class RunProgressCounterTests
{
  [Test]
  public async Task Record_NoneOutcomeCountsAsInconclusive()
  {
    var counter = new RunProgressCounter(1);

    counter.Record("none");

    var snapshot = counter.Snapshot();
    await Assert.That(snapshot.Running).IsEqualTo(0);
    await Assert.That(snapshot.Failed).IsEqualTo(0);
    await Assert.That(snapshot.Skipped).IsEqualTo(0);
    await Assert.That(snapshot.Inconclusive).IsEqualTo(1);
  }
}
