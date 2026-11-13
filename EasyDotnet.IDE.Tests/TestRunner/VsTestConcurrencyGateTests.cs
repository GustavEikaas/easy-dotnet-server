using EasyDotnet.IDE.TestRunner.Adapters;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class VsTestConcurrencyGateTests
{
  [Test]
  public async Task AcquireAsync_SerializesConcurrentCallers()
  {
    var gate = new VsTestConcurrencyGate();
    var concurrentCount = 0;
    var maxObservedConcurrency = 0;
    var gate2 = new object();

    var tasks = Enumerable.Range(0, 5).Select(async _ =>
    {
      await using var lease = await gate.AcquireAsync(CancellationToken.None);

      lock (gate2)
      {
        concurrentCount++;
        maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentCount);
      }

      await Task.Delay(50);

      lock (gate2)
      {
        concurrentCount--;
      }
    });

    await Task.WhenAll(tasks);

    await Assert.That(maxObservedConcurrency).IsEqualTo(1);
  }

  [Test]
  public async Task AcquireAsync_ReleasesOnDispose_AllowingNextCallerToProceed()
  {
    var gate = new VsTestConcurrencyGate();

    var firstLease = await gate.AcquireAsync(CancellationToken.None);
    var secondAcquired = false;

    var secondAcquireTask = Task.Run(async () =>
    {
      await using var lease = await gate.AcquireAsync(CancellationToken.None);
      secondAcquired = true;
    });

    await Task.Delay(50);
    await Assert.That(secondAcquired).IsFalse();

    await firstLease.DisposeAsync();
    await secondAcquireTask;

    await Assert.That(secondAcquired).IsTrue();
  }

  [Test]
  public async Task AcquireAsync_CancelledToken_ThrowsAndDoesNotAcquire()
  {
    var gate = new VsTestConcurrencyGate();
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(async () => await gate.AcquireAsync(cts.Token));

    await using var lease = await gate.AcquireAsync(CancellationToken.None);
  }
}
