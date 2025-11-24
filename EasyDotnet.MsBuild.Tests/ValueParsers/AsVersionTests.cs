namespace EasyDotnet.MsBuild.Tests.ValueParsers;

public class AsVersionTests
{
  [Test]
  public async Task AsVersion_ReturnsVersion_ForValidVersionString()
  {
    var dict = new Dictionary<string, string?> { ["Ver"] = "8.0.8" };
    var result = MsBuildValueParsers.AsVersion(dict, "Ver");
    await Assert.That(result).IsEqualTo(new Version("8.0.8"));
  }

  [Test]
  public async Task AsVersion_ReturnsNull_ForEmptyString()
  {
    var dict = new Dictionary<string, string?> { ["Ver"] = "" };
    var result = MsBuildValueParsers.AsVersion(dict, "Ver");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsVersion_ReturnsNull_ForWhitespace()
  {
    var dict = new Dictionary<string, string?> { ["Ver"] = "   " };
    var result = MsBuildValueParsers.AsVersion(dict, "Ver");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsVersion_ReturnsNull_ForInvalidString()
  {
    var dict = new Dictionary<string, string?> { ["Ver"] = "not.a.version" };
    var result = MsBuildValueParsers.AsVersion(dict, "Ver");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsVersion_ReturnsNull_WhenKeyMissing()
  {
    var dict = new Dictionary<string, string?>();
    var result = MsBuildValueParsers.AsVersion(dict, "Ver");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsVersion_ReturnsNull_WhenValueIsNull()
  {
    var dict = new Dictionary<string, string?> { ["Ver"] = null };
    var result = MsBuildValueParsers.AsVersion(dict, "Ver");
    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task AsVersion_ReturnsVersion_ForShortVersion()
  {
    var dict = new Dictionary<string, string?> { ["Ver"] = "1.2" };
    var result = MsBuildValueParsers.AsVersion(dict, "Ver");
    await Assert.That(result).IsEqualTo(new Version(1, 2));
  }

  [Test]
  public async Task AsVersion_ReturnsVersion_ForFourPartVersion()
  {
    var dict = new Dictionary<string, string?> { ["Ver"] = "1.2.3.4" };
    var result = MsBuildValueParsers.AsVersion(dict, "Ver");
    await Assert.That(result).IsEqualTo(new Version(1, 2, 3, 4));
  }
}