using EasyDotnet.Infrastructure.TestRunner;

namespace EasyDotnet.Infrastructure.Tests.TestRunner;

public class TestLocatorTests
{
  private const string OriginalCode = @"
using EasyDotnet.Debugger.Messages;
namespace EasyDotnet.Debugger.Tests.Dap;

public class DapMessageDeserializerTests
{
  [Test] // -> Line 7 (1-based)
  public async Task Parse_ValidRequestJson_ReturnsRequest()
  {
     // ... body ...
  }

  [Test]
  public async Task Parse_ValidEventJson_ReturnsEvent()
  {
     // ... body ...
  }
}";

  private const string SwappedCode = @"
using EasyDotnet.Debugger.Messages;
namespace EasyDotnet.Debugger.Tests.Dap;

public class DapMessageDeserializerTests
{
  [Test]
  public async Task Parse_ValidEventJson_ReturnsEvent()
  {
     // ... body ...
  }

  [Test] // -> Line 14 (1-based)
  public async Task Parse_ValidRequestJson_ReturnsRequest()
  {
     // ... body ...
  }
}";

  [Test]
  public async Task GetMethodSignature_ReturnsCorrectId_FromAttributeLine()
  {
    // Line 7 is the [Test] attribute for the first method in OriginalCode
    var signature = RoslynLocator.GetMethodSignatureAtLine(OriginalCode, 7);

    await Assert.That(signature).IsEqualTo("DapMessageDeserializerTests.Parse_ValidRequestJson_ReturnsRequest");
  }

  [Test]
  public async Task GetMethodSignature_ReturnsCorrectId_FromMethodBody()
  {
    // Line 9 is inside the body of the first method in OriginalCode
    // "public async Task..." is line 8, "{" is line 9
    var signature = RoslynLocator.GetMethodSignatureAtLine(OriginalCode, 9);

    await Assert.That(signature).IsEqualTo("DapMessageDeserializerTests.Parse_ValidRequestJson_ReturnsRequest");
  }

  [Test]
  public async Task FindLineForMethod_RefindsMethod_AfterSwap()
  {
    var signature = "DapMessageDeserializerTests.Parse_ValidRequestJson_ReturnsRequest";

    // In SwappedCode, this method is now lower down
    var newLine = RoslynLocator.FindLineForMethod(SwappedCode, signature);

    await Assert.That(newLine).IsNotNull();
    // In the swapped string above, the [Test] attribute for this method is on line 14
    await Assert.That(newLine).IsEqualTo(13);
  }

  [Test]
  public async Task FindLineForMethod_ReturnsNull_WhenMethodRemoved()
  {
    var codeWithMissingMethod = @"
namespace EasyDotnet.Debugger.Tests.Dap;
public class DapMessageDeserializerTests
{
  // Method deleted
}";
    var signature = "DapMessageDeserializerTests.Parse_ValidRequestJson_ReturnsRequest";

    var newLine = RoslynLocator.FindLineForMethod(codeWithMissingMethod, signature);

    await Assert.That(newLine).IsNull();
  }
}