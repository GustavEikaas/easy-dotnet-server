using EasyDotnet.Infrastructure.Services;

namespace EasyDotnet.Infrastructure.Tests.Debugger;

[NotInParallel]
public class DebuggerLocatorTests
{
  //No bundled version when running locally
  [Test]
  public void GetDebuggerPath_UsesBundledVersion_WhenNoEnvVarSet()
  {
    Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    Assert.Throws<FileNotFoundException>(() => NetCoreDbgLocator.GetNetCoreDbgPath());
  }

  [Test]
  public async Task GetDebuggerPath_UsesCustomPath_WhenEnvVarSet()
  {
    var customDebuggerPath = Path.GetTempFileName();
    try
    {
      Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, customDebuggerPath);
      var path = NetCoreDbgLocator.GetNetCoreDbgPath();
      await Assert.That(path).IsEqualTo(customDebuggerPath);
    }
    finally
    {
      File.Delete(customDebuggerPath);
      Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    }
  }

  [After(Test)]
  public void Cleanup() => Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
}