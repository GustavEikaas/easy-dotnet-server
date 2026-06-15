using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.Tests.Debugger;

[NotInParallel]
public class DebuggerLocatorTests
{
  //No bundled version when running locally
  [Test]
  public void GetDebuggerPath_UsesBundledVersion_WhenNoEnvVarSet()
  {
    Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, null);
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

  [Test]
  public async Task ResolveDebugger_DefaultsToNetCoreDbg()
  {
    Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, null);

    var engine = DebuggerLocator.GetConfiguredEngine();

    await Assert.That(engine).IsEqualTo(DebuggerEngine.NetCoreDbg);
  }

  [Test]
  public async Task ResolveDebugger_UsesDncDbgEngine_WhenEnvVarSet()
  {
    Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, "dncdbg");

    var engine = DebuggerLocator.GetConfiguredEngine();

    await Assert.That(engine).IsEqualTo(DebuggerEngine.DncDbg);
  }

  [Test]
  public async Task ResolveDebugger_UsesCustomPath_WhenDncDbgEngineSet()
  {
    var customDebuggerPath = Path.GetTempFileName();
    try
    {
      Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, customDebuggerPath);
      Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, "dncdbg");

      var debugger = DebuggerLocator.ResolveDebugger();

      await Assert.That(debugger.Engine).IsEqualTo(DebuggerEngine.DncDbg);
      await Assert.That(debugger.Source).IsEqualTo(DebuggerLocator.DEBUGGER_PATH_ENV);
      await Assert.That(debugger.Path).IsEqualTo(customDebuggerPath);
    }
    finally
    {
      File.Delete(customDebuggerPath);
      Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
      Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, null);
    }
  }

  [Test]
  public async Task ResolveDebugger_UsesBundledDncDbgPath_WhenEngineSet()
  {
    Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, "dncdbg");

    var platform = DebuggerLocator.GetRuntimePlatform();
    var expectedPath = DebuggerLocator.GetBundledDebuggerPath(DebuggerEngine.DncDbg, platform);
    var existed = File.Exists(expectedPath);

    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
      if (!existed)
      {
        await File.WriteAllTextAsync(expectedPath, "");
      }

      var debugger = DebuggerLocator.ResolveDebugger();

      await Assert.That(debugger.Engine).IsEqualTo(DebuggerEngine.DncDbg);
      await Assert.That(debugger.Source).IsEqualTo("bundled");
      await Assert.That(debugger.Platform).IsEqualTo(platform);
      await Assert.That(debugger.Path).IsEqualTo(expectedPath);
    }
    finally
    {
      if (!existed && File.Exists(expectedPath))
      {
        File.Delete(expectedPath);
      }

      Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
      Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, null);
    }
  }

  [Test]
  public void ResolveDebugger_ThrowsForInvalidEngine()
  {
    Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, "invalid");

    Assert.Throws<ArgumentException>(() => DebuggerLocator.GetConfiguredEngine());
  }

  [Test]
  public async Task ParseEngine_RecognisesSharpDbg()
  {
    await Assert.That(DebuggerLocator.ParseEngine("sharpdbg")).IsEqualTo(DebuggerEngine.SharpDbg);
    await Assert.That(DebuggerLocator.ParseEngine("sharp")).IsEqualTo(DebuggerEngine.SharpDbg);
  }

  [Test]
  public async Task ParseEngine_RecognisesCustom()
  {
    await Assert.That(DebuggerLocator.ParseEngine("custom")).IsEqualTo(DebuggerEngine.Custom);
  }

  [Test]
  public async Task ResolveDebugger_UsesSharpDbgEngine_WhenEnvVarSet()
  {
    Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, "sharpdbg");

    var engine = DebuggerLocator.GetConfiguredEngine();

    await Assert.That(engine).IsEqualTo(DebuggerEngine.SharpDbg);
  }

  [Test]
  public async Task ResolveDebugger_CustomEngine_UsesPathFromEnvVar()
  {
    var customPath = Path.GetTempFileName();
    try
    {
      Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, customPath);
      Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, "custom");

      var debugger = DebuggerLocator.ResolveDebugger();

      await Assert.That(debugger.Engine).IsEqualTo(DebuggerEngine.Custom);
      await Assert.That(debugger.Path).IsEqualTo(customPath);
    }
    finally
    {
      File.Delete(customPath);
      Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
      Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, null);
    }
  }

  [Test]
  public void ResolveDebugger_CustomEngine_ThrowsWhenNoBinaryPath()
  {
    Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, "custom");

    Assert.Throws<InvalidOperationException>(() => DebuggerLocator.ResolveDebugger());
  }

  [After(Test)]
  public void Cleanup()
  {
    Environment.SetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV, null);
    Environment.SetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV, null);
  }
}