namespace EasyDotnet.ContainerTests;

public sealed record TestServerInfo(string Name, string Version);
public sealed record TestServerCapabilities(List<string> Routes, List<string> ServerSentNotifications, bool SupportsSingleFileExecution);
public sealed record TestInitializeResponse(TestServerInfo ServerInfo, TestServerCapabilities Capabilities);
public sealed record TestInitializeRequest(TestClientInfo ClientInfo, TestProjectInfo ProjectInfo);
public sealed record TestProjectInfo(string RootDir, string? SolutionFile = null);
public sealed record TestClientInfo(string Name, string? Version);

// Reverse-request payloads (server → client)
public sealed record TestSelectionOption(string Id, string Display);
public sealed record TestPromptSelectionRequest(
  string Prompt,
  TestSelectionOption[] Choices,
  string? DefaultSelectionId);

public sealed record TestRunCommand(
  string Executable,
  List<string> Arguments,
  string WorkingDirectory,
  Dictionary<string, string> EnvironmentVariables);

public sealed record TestTrackedJob(Guid JobId, TestRunCommand Command);

public sealed record TestDisplayMessage(string Message);

public sealed record TestQuickFixItem(
  string FileName,
  int LineNumber,
  int ColumnNumber,
  string Text,
  TestQuickFixItemType Type);

public enum TestQuickFixItemType
{
  Information = 0,
  Warning = 1,
  Error = 2
}