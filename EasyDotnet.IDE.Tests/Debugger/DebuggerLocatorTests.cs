using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.Tests.Debugger;

[NotInParallel]
public class DebuggerLocatorTests
{
  //No bundled version when running locally
  [Test]
  public void GetDebuggerPath_UsesBundledVersion_WhenNoEnvVarSet()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, null);
    Assert.Throws<FileNotFoundException>(() => NetCoreDbgLocator.GetNetCoreDbgPath());
  }

  [Test]
  public async Task GetDebuggerPath_UsesCustomPath_WhenEnvVarSet()
  {
    var customDebuggerPath = Path.GetTempFileName();
    try
    {
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, customDebuggerPath);
      var path = DebuggerLocator.ResolveDebugger(null, null);
      await Assert.That(path.Path).IsEqualTo(customDebuggerPath);
    }
    finally
    {
      File.Delete(customDebuggerPath);
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    }
  }

  [Test]
  public async Task ResolveDebugger_DefaultsToNetCoreDbg()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, null);

    var engine = DebuggerLocator.GetConfiguredEngine();

    await Assert.That(engine).IsEqualTo(DebuggerEngine.NetCoreDbg);
  }

  [Test]
  public async Task ResolveDebugger_UsesDncDbgEngine_WhenEnvVarSet()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, "dncdbg");

    var engine = DebuggerLocator.GetConfiguredEngine();

    await Assert.That(engine).IsEqualTo(DebuggerEngine.DncDbg);
  }

  [Test]
  public async Task ResolveDebugger_CustomPathOverridesConfiguredEngine()
  {
    var customDebuggerPath = Path.GetTempFileName();
    try
    {
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, customDebuggerPath);
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, "dncdbg");

      var debugger = DebuggerLocator.ResolveDebugger();

      // A custom binary path is run as the Custom engine, ignoring the configured engine.
      await Assert.That(debugger.Engine).IsEqualTo(DebuggerEngine.Custom);
      await Assert.That(debugger.Source).IsEqualTo(WellKnownEnvironment.DebuggerBinPath.Name);
      await Assert.That(debugger.Path).IsEqualTo(customDebuggerPath);
      await Assert.That(debugger.FileName).IsEqualTo(customDebuggerPath);
      await Assert.That(debugger.Arguments).IsEquivalentTo(["--interpreter=vscode"]);
    }
    finally
    {
      File.Delete(customDebuggerPath);
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, null);
    }
  }

  [Test]
  public async Task ResolveDebugger_UsesBundledDncDbgPath_WhenEngineSet()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, "dncdbg");

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

      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, null);
    }
  }

  [Test]
  public async Task ResolveDebugger_UsesSharpDbgEngine_WhenEnvVarSet()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, "sharpdbg");

    var engine = DebuggerLocator.GetConfiguredEngine();

    await Assert.That(engine).IsEqualTo(DebuggerEngine.SharpDbg);
  }

  [Test]
  public async Task ResolveDebugger_LaunchesSharpDbgViaDotnetMuxer_WhenEngineSet()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, "sharpdbg");

    var platform = DebuggerLocator.GetRuntimePlatform();
    var expectedPath = DebuggerLocator.GetBundledDebuggerPath(DebuggerEngine.SharpDbg, platform);
    var existed = File.Exists(expectedPath);

    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
      if (!existed)
      {
        await File.WriteAllTextAsync(expectedPath, "");
      }

      var debugger = DebuggerLocator.ResolveDebugger();

      await Assert.That(debugger.Engine).IsEqualTo(DebuggerEngine.SharpDbg);
      await Assert.That(debugger.Source).IsEqualTo("bundled");
      await Assert.That(debugger.Path).IsEqualTo(expectedPath);
      // SharpDbg is a managed dll, launched through the dotnet muxer.
      await Assert.That(debugger.FileName).IsEqualTo("dotnet");
      await Assert.That(debugger.Arguments).IsEquivalentTo(new[] { expectedPath, "--interpreter=vscode" });
    }
    finally
    {
      if (!existed && File.Exists(expectedPath))
      {
        File.Delete(expectedPath);
      }

      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, null);
    }
  }

  [Test]
  public async Task GetLaunchCommand_LaunchesNativeDebuggerDirectly()
  {
    var (fileName, arguments) = DebuggerLocator.GetLaunchCommand(DebuggerEngine.NetCoreDbg, "/tools/netcoredbg/netcoredbg");

    await Assert.That(fileName).IsEqualTo("/tools/netcoredbg/netcoredbg");
    await Assert.That(arguments).IsEquivalentTo(new[] { "--interpreter=vscode" });
  }

  [Test]
  public async Task GetLaunchCommand_LaunchesSharpDbgViaDotnetMuxer()
  {
    var (fileName, arguments) = DebuggerLocator.GetLaunchCommand(DebuggerEngine.SharpDbg, "/tools/sharpdbg/SharpDbg.Cli.dll");

    await Assert.That(fileName).IsEqualTo("dotnet");
    await Assert.That(arguments).IsEquivalentTo(new[] { "/tools/sharpdbg/SharpDbg.Cli.dll", "--interpreter=vscode" });
  }

  [Test]
  public async Task GetVersionCommand_RoutesThroughTheEngineRunner()
  {
    var (nativeFileName, nativeArgs) = DebuggerLocator.GetVersionCommand(DebuggerEngine.NetCoreDbg, "/tools/netcoredbg/netcoredbg");
    await Assert.That(nativeFileName).IsEqualTo("/tools/netcoredbg/netcoredbg");
    await Assert.That(nativeArgs).IsEquivalentTo(new[] { "--version" });

    var (sharpFileName, sharpArgs) = DebuggerLocator.GetVersionCommand(DebuggerEngine.SharpDbg, "/tools/sharpdbg/SharpDbg.Cli.dll");
    await Assert.That(sharpFileName).IsEqualTo("dotnet");
    await Assert.That(sharpArgs).IsEquivalentTo(new[] { "/tools/sharpdbg/SharpDbg.Cli.dll", "--version" });
  }

  [Test]
  public async Task GetConfiguredEngine_UsesCustom_WhenBinaryPathProvidedWithoutEngine()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, null);

    var engine = DebuggerLocator.GetConfiguredEngine(engineName: null, debuggerBinPath: "/path/to/vsdbg");

    await Assert.That(engine).IsEqualTo(DebuggerEngine.Custom);
  }

  [Test]
  public async Task ResolveDebugger_UsesCustomEngine_ForCustomBinaryPathWithoutEngine()
  {
    var customDebuggerPath = Path.GetTempFileName();
    try
    {
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, null);

      var debugger = DebuggerLocator.ResolveDebugger(engineName: null, debuggerBinPath: customDebuggerPath);

      await Assert.That(debugger.Engine).IsEqualTo(DebuggerEngine.Custom);
      await Assert.That(debugger.Source).IsEqualTo("--debugger-bin-path");
      await Assert.That(debugger.Path).IsEqualTo(customDebuggerPath);
      // Optimistically run the binary directly as a DAP server.
      await Assert.That(debugger.FileName).IsEqualTo(customDebuggerPath);
      await Assert.That(debugger.Arguments).IsEquivalentTo(new[] { "--interpreter=vscode" });
    }
    finally
    {
      File.Delete(customDebuggerPath);
      Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    }
  }

  [Test]
  public async Task GetLaunchCommand_RunsCustomEngineDirectly()
  {
    var (fileName, arguments) = DebuggerLocator.GetLaunchCommand(DebuggerEngine.Custom, "/path/to/vsdbg");

    await Assert.That(fileName).IsEqualTo("/path/to/vsdbg");
    await Assert.That(arguments).IsEquivalentTo(new[] { "--interpreter=vscode" });
  }

  [Test]
  public void ResolveDebugger_ThrowsForInvalidEngine()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, "invalid");

    Assert.Throws<ArgumentException>(() => DebuggerLocator.GetConfiguredEngine());
  }

  [After(Test)]
  public void Cleanup()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerBinPath.Name, null);
    Environment.SetEnvironmentVariable(WellKnownEnvironment.DebuggerEngine.Name, null);
  }
}