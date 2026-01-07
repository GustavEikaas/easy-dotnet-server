using EasyDotnet.Infrastructure.Services;

namespace EasyDotnet.Infrastructure.Tests.Roslyn;

[NotInParallel]
public class RoslynLocatorTests
{

  //No bundled version when running locally
  [Test]
  public async Task GetRoslynDllPath_UsesBundledVersion_WhenNoEnvVarSet()
  {

    Environment.SetEnvironmentVariable(RoslynLocator.ROSLYN_DLL_PATH_ENV, null);
    Assert.Throws(() => RoslynLocator.GetRoslynDllPath());
  }

  [Test]
  public async Task GetRoslynDllPath_UsesCustomPath_WhenEnvVarSet()
  {
    var customDllPath = Path.GetTempFileName();
    try
    {
      Environment.SetEnvironmentVariable(RoslynLocator.ROSLYN_DLL_PATH_ENV, customDllPath);
      var path = RoslynLocator.GetRoslynDllPath();
      await Assert.That(path).IsEqualTo(customDllPath);
    }
    finally
    {
      File.Delete(customDllPath);
    }
  }

  [After(Test)]
  public void Cleanup() => Environment.SetEnvironmentVariable(RoslynLocator.ROSLYN_DLL_PATH_ENV, null);
}