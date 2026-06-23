namespace EasyDotnet.IDE.Services;

public sealed class SharpDbgDefinition : IDebuggerEngineDefinition
{
  private const string Dll = "SharpDbg.Cli.dll";

  public DebuggerEngine Engine => DebuggerEngine.SharpDbg;
  public string Name => "sharpdbg";

  public string GetBundledRelativePath(string platform) => Path.Combine(Name, Dll);

  public (string FileName, IReadOnlyList<string> Arguments) BuildLaunchCommand(string debuggerPath) =>
    ("dotnet", [debuggerPath, "--interpreter=vscode"]);

  public (string FileName, IReadOnlyList<string> Arguments) BuildVersionCommand(string debuggerPath) =>
    ("dotnet", [debuggerPath, "--version"]);
}