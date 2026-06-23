namespace EasyDotnet.IDE.Services;

public sealed class DncDbgDefinition : IDebuggerEngineDefinition
{
  public DebuggerEngine Engine => DebuggerEngine.DncDbg;
  public string Name => "dncdbg";

  public string GetBundledRelativePath(string platform) =>
    Path.Combine(Name, platform, OperatingSystem.IsWindows() ? "dncdbg.exe" : "dncdbg");

  public (string FileName, IReadOnlyList<string> Arguments) BuildLaunchCommand(string debuggerPath) =>
    (debuggerPath, ["--interpreter=vscode"]);

  public (string FileName, IReadOnlyList<string> Arguments) BuildVersionCommand(string debuggerPath) =>
    (debuggerPath, ["--version"]);
}