using EasyDotnet.MsBuild.ProjectModel;

namespace EasyDotnet.MsBuild.Tests.ValueParsers;

public class AsPathTests
{

  [Test]
  public async Task AsPath_ReturnsNormalizedPath()
  {
    var dict = new Dictionary<string, string?> { ["OutDir"] = @"\bin\net8.0\" };
    var result = MsBuildValueParsers.AsPath(dict, "OutDir");
    await Assert.That(result).IsEqualTo("/bin/net8.0/");
  }

  [Test]
  public async Task AsPath_DoesNotModifyForwardSlashes()
  {
    var dict = new Dictionary<string, string?> { ["OutDir"] = "/bin/net8.0/" };
    var result = MsBuildValueParsers.AsPath(dict, "OutDir");
    await Assert.That(result).IsEqualTo("/bin/net8.0/");
  }

  [Test]
  public async Task AsPath_NormalizesMixedPathSeparators()
  {
    var dict = new Dictionary<string, string?> { ["OutDir"] = @"/bin\net8.0\" };
    var result = MsBuildValueParsers.AsPath(dict, "OutDir");
    await Assert.That(result).IsEqualTo("/bin/net8.0/");
  }
}