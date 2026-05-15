using EasyDotnet.IDE.TestRunner.Executor;
using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class TestStatusPriorityTests
{
  [Test]
  public async Task AggregateTerminalStatus_PassedOutranksInconclusive()
  {
    var status = TestStatusPriority.AggregateTerminalStatus(
        passed: 1,
        failed: 0,
        skipped: 0,
        inconclusive: 1,
        durationDisplay: "5 ms");

    await Assert.That(status.Kind).IsEqualTo(TestNodeStatusKind.Passed);
  }

  [Test]
  public async Task AggregateTerminalStatus_SkippedOutranksInconclusive()
  {
    var status = TestStatusPriority.AggregateTerminalStatus(
        passed: 0,
        failed: 0,
        skipped: 1,
        inconclusive: 1,
        durationDisplay: "5 ms");

    await Assert.That(status.Kind).IsEqualTo(TestNodeStatusKind.Skipped);
  }

  [Test]
  public async Task AggregateTerminalStatus_FailedOutranksPassed()
  {
    var status = TestStatusPriority.AggregateTerminalStatus(
        passed: 1,
        failed: 1,
        skipped: 1,
        inconclusive: 1,
        durationDisplay: "5 ms");

    await Assert.That(status.Kind).IsEqualTo(TestNodeStatusKind.Failed);
  }

  [Test]
  public async Task AggregateTerminalStatus_AllInconclusiveStaysInconclusive()
  {
    var status = TestStatusPriority.AggregateTerminalStatus(
        passed: 0,
        failed: 0,
        skipped: 0,
        inconclusive: 1,
        durationDisplay: "5 ms");

    await Assert.That(status.Kind).IsEqualTo(TestNodeStatusKind.Inconclusive);
  }
}