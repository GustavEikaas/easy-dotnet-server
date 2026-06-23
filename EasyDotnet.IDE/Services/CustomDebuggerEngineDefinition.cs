namespace EasyDotnet.IDE.Services;

/// <summary>
/// Fallback engine for a user-supplied debugger binary (e.g. vsdbg). It has no bundled payload and is
/// run optimistically as a DAP server: the executable directly with <c>--interpreter=vscode</c>.
/// </summary>
public sealed class CustomDebuggerEngineDefinition : IDebuggerEngineDefinition
{
  public DebuggerEngine Engine => DebuggerEngine.Custom;
  public string Name => "custom";

  public string GetBundledRelativePath(string platform) =>
    throw new NotSupportedException("The custom debugger engine has no bundled binary; provide a debugger binary path.");

  public (string FileName, IReadOnlyList<string> Arguments) BuildLaunchCommand(string debuggerPath) =>
    (debuggerPath, ["--interpreter=vscode"]);

  public (string FileName, IReadOnlyList<string> Arguments) BuildVersionCommand(string debuggerPath) =>
    (debuggerPath, ["--version"]);
}