namespace EasyDotnet.MsBuild.Tests.ValueParsers;

public class AsBoolTests
{

  [Test]
  public async Task AsBool_ReturnsTrueForTrue_lowercase()
  {
    var dict = new Dictionary<string, string?> { ["Build"] = "true" };
    var result = MsBuildValueParsers.AsBool(dict, "Build");
    await Assert.That(result).IsEqualTo(true);
  }

  [Test]
  public async Task AsBool_ReturnsTrueForTrue_uppercase()
  {
    var dict = new Dictionary<string, string?> { ["Build"] = "True" };
    var result = MsBuildValueParsers.AsBool(dict, "Build");
    await Assert.That(result).IsEqualTo(true);
  }

  [Test]
  public async Task AsBool_ReturnsFalseForFalse_lowercase()
  {
    var dict = new Dictionary<string, string?> { ["Build"] = "false" };
    var result = MsBuildValueParsers.AsBool(dict, "Build");
    await Assert.That(result).IsEqualTo(false);
  }

  [Test]
  public async Task AsBool_ReturnsFalseForFalse_uppercase()
  {
    var dict = new Dictionary<string, string?> { ["Build"] = "False" };
    var result = MsBuildValueParsers.AsBool(dict, "Build");
    await Assert.That(result).IsEqualTo(false);
  }

  [Test]
  public async Task AsBool_ReturnsFalseForWhitespace()
  {
    var dict = new Dictionary<string, string?> { ["Build"] = "" };
    var result = MsBuildValueParsers.AsBool(dict, "Build");
    await Assert.That(result).IsEqualTo(false);
  }

  [Test]
  public async Task AsBool_ReturnsFalseForRandomStringValue()
  {
    var dict = new Dictionary<string, string?> { ["Build"] = "/bin/path" };
    var result = MsBuildValueParsers.AsBool(dict, "Build");
    await Assert.That(result).IsEqualTo(false);
  }

  [Test]
  public async Task AsBool_ReturnsFalseForNonExistingKey()
  {
    var dict = new Dictionary<string, string?>();
    var result = MsBuildValueParsers.AsBool(dict, "Build");
    await Assert.That(result).IsEqualTo(false);
  }

  [Test]
  public async Task AsBool_ReturnsFalseForNullValue()
  {
    var dict = new Dictionary<string, string?> { ["Build"] = null };
    var result = MsBuildValueParsers.AsBool(dict, "Build");
    await Assert.That(result).IsEqualTo(false);
  }

  [Test]
  public async Task AsBool_ReturnsFalse_WhenValueHasWhitespaceAroundIt()
  {
    var dict = new Dictionary<string, string?> { ["Build"] = " true " };
    await Assert.That(MsBuildValueParsers.AsBool(dict, "Build")).IsTrue();
  }
}
