namespace EasyDotnet.MsBuild.Tests.ValueParsers;

public class AsStringListTests
{
  [Test]
  public async Task AsStringList_ReturnsArray_ForValidSemicolonSeparatedString()
  {
    var dict = new Dictionary<string, string?> { ["List"] = "a;b;c" };
    var result = MsBuildValueParsers.AsStringList(dict, "List");
    await Assert.That(result).IsEquivalentTo(["a", "b", "c"]);
  }

  [Test]
  public async Task AsStringList_TrimsWhitespaceAroundItems()
  {
    var dict = new Dictionary<string, string?> { ["List"] = " a ; b ;c " };
    var result = MsBuildValueParsers.AsStringList(dict, "List");
    await Assert.That(result).IsEquivalentTo(["a", "b", "c"]);
  }

  [Test]
  public async Task AsStringList_RemovesEmptyEntries()
  {
    var dict = new Dictionary<string, string?> { ["List"] = "a;;b;;" };
    var result = MsBuildValueParsers.AsStringList(dict, "List");
    await Assert.That(result).IsEquivalentTo(["a", "b"]);
  }

  [Test]
  public async Task AsStringList_ReturnsEmptyArray_ForEmptyString()
  {
    var dict = new Dictionary<string, string?> { ["List"] = "" };
    var result = MsBuildValueParsers.AsStringList(dict, "List");
    await Assert.That(result).IsEmpty();
  }

  [Test]
  public async Task AsStringList_ReturnsEmptyArray_ForWhitespaceString()
  {
    var dict = new Dictionary<string, string?> { ["List"] = "   " };
    var result = MsBuildValueParsers.AsStringList(dict, "List");
    await Assert.That(result).IsEmpty();
  }

  [Test]
  public async Task AsStringList_ReturnsEmptyArray_WhenKeyMissing()
  {
    var dict = new Dictionary<string, string?>();
    var result = MsBuildValueParsers.AsStringList(dict, "List");
    await Assert.That(result).IsEmpty();
  }

  [Test]
  public async Task AsStringList_ReturnsEmptyArray_WhenValueIsNull()
  {
    var dict = new Dictionary<string, string?> { ["List"] = null };
    var result = MsBuildValueParsers.AsStringList(dict, "List");
    await Assert.That(result).IsEmpty();
  }

  [Test]
  public async Task AsStringList_HandlesLeadingAndTrailingSemicolons()
  {
    var dict = new Dictionary<string, string?> { ["List"] = ";a;b;c;" };
    var result = MsBuildValueParsers.AsStringList(dict, "List");
    await Assert.That(result).IsEquivalentTo(["a", "b", "c"]);
  }
}
