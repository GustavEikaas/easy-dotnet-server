namespace EasyDotnet.IDE.Services;

public interface IDebuggerEngineDefinition
{
  DebuggerEngine Engine { get; }

  string Name { get; }

  string GetBundledRelativePath(string platform);

  (string FileName, IReadOnlyList<string> Arguments) BuildLaunchCommand(string debuggerPath);

  (string FileName, IReadOnlyList<string> Arguments) BuildVersionCommand(string debuggerPath);
}