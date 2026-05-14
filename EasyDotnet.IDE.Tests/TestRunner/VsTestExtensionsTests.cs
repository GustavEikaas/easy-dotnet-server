using EasyDotnet.IDE.TestRunner.Adapters;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class VsTestExtensionsTests
{
  [Test]
  public async Task ToTestRunResult_MapsNoneOutcomeToNone()
  {
    var testCase = new TestCase("Sample.Tests.Case", new Uri("executor://sample"), "Sample.Tests.dll");
    var result = new Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult(testCase)
    {
      Outcome = TestOutcome.None
    };

    var mapped = result.ToTestRunResult();

    await Assert.That(mapped).IsNotNull();
    await Assert.That(mapped!.Outcome).IsEqualTo("none");
  }
}
