namespace EasyDotnet.IDE.Services;

public sealed class NetCoreDbgDefinition : IDebuggerEngineDefinition
{
  public DebuggerEngine Engine => DebuggerEngine.NetCoreDbg;
  public string Name => "netcoredbg";

  public string GetBundledRelativePath(string platform) =>
    Path.Combine(Name, platform, OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg");

  public (string FileName, IReadOnlyList<string> Arguments) BuildLaunchCommand(string debuggerPath) =>
    (debuggerPath, ["--interpreter=vscode"]);

  public (string FileName, IReadOnlyList<string> Arguments) BuildVersionCommand(string debuggerPath) =>
    (debuggerPath, ["--version"]);
}