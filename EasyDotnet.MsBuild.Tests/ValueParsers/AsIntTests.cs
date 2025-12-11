using EasyDotnet.MsBuild.ProjectModel;

namespace EasyDotnet.MsBuild.Tests.ValueParsers;

public class AsIntTests
{
  [Test]
  public async Task AsInt_ReturnsInt_ForValidInteger()
  {
    var dict = new Dictionary<string, string?> { ["Num"] = "42" };
    var result = MsBuildValueParsers.AsInt(dict, "Num");
    await Assert.That(result).IsEqualTo(42);
  }

  [Test]
  public async Task AsInt_ReturnsInt_ForNegativeInteger()
  {
    var dict = new Dictionary<string, string?> { ["Num"] = "-7" };
    var result = MsBuildValueParsers.AsInt(dict, "Num");
    await Assert.That(result).IsEqualTo(-7);
  }

  [Test]
  public async Task AsInt_ReturnsInt_ForZero()
  {
    var dict = new Dictionary<string, string?> { ["Num"] = "0" };
    var result = MsBuildValueParsers.AsInt(dict, "Num");
    await Assert.That(result).IsEqualTo(0);
  }

  [Test]
  public async Task AsInt_ReturnsNull_ForInvalidString()
  {
    var dict = new Dictionary<string, string?> { ["Num"] = "notanumber" };
    var result = MsBuildValueParsers.AsInt(dict, "Num");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsInt_ReturnsNull_ForEmptyString()
  {
    var dict = new Dictionary<string, string?> { ["Num"] = "" };
    var result = MsBuildValueParsers.AsInt(dict, "Num");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsInt_ReturnsNull_ForWhitespace()
  {
    var dict = new Dictionary<string, string?> { ["Num"] = "   " };
    var result = MsBuildValueParsers.AsInt(dict, "Num");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsInt_ReturnsNull_WhenKeyMissing()
  {
    var dict = new Dictionary<string, string?>();
    var result = MsBuildValueParsers.AsInt(dict, "Num");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsInt_ReturnsNull_WhenValueIsNull()
  {
    var dict = new Dictionary<string, string?> { ["Num"] = null };
    var result = MsBuildValueParsers.AsInt(dict, "Num");
    await Assert.That(result).IsNull();
  }
}