using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.Tests.Roslyn;

[NotInParallel]
public class RoslynLocatorTests
{

  [Test]
  public async Task TryParseVersion_StripsBuildMetadata()
  {
    var version = RoslynToolService.TryParseVersion("5.8.0-1.26252.1+3d098b3a2f24112aa06731d38ea6dd7334169998");
    await Assert.That(version).IsNotNull();
    await Assert.That(version!.ToString()).IsEqualTo("5.8.0-1.26252.1");
  }

  [Test]
  public async Task IsBelowRecommendedVersion_UsesMinimumRecommendedVersion()
  {
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.8.0-1.26252.1")).IsTrue();
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.8.0-1.26262.10")).IsFalse();
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.8.0-1.26266.2")).IsFalse();
  }

  [After(Test)]
  public void Cleanup() => Environment.SetEnvironmentVariable(RoslynLocator.ROSLYN_DLL_PATH_ENV, null);
}