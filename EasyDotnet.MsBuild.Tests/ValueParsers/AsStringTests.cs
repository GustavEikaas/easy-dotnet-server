namespace EasyDotnet.MsBuild.Tests.ValueParsers;

public class AsStringTests
{
  [Test]
  public async Task AsString_ReturnsValue()
  {
    var dict = new Dictionary<string, string?> { ["Name"] = "Value" };
    var result = MsBuildValueParsers.AsString(dict, "Name");
    await Assert.That(result).IsEqualTo("Value");
  }

  [Test]
  public async Task AsString_ReturnsNullForWhitespace()
  {
    var dict = new Dictionary<string, string?> { ["Name"] = "" };
    var result = MsBuildValueParsers.AsString(dict, "Name");
    await Assert.That(result).IsEqualTo(null);
  }
}