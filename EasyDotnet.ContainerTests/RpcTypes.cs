namespace EasyDotnet.ContainerTests;

public sealed record TestServerInfo(string Name, string Version);
public sealed record TestServerCapabilities(List<string> Routes, List<string> ServerSentNotifications);
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

// Settings file shapes (for reading /tmp/.local/share/easy-dotnet/solution_*.json)
public sealed class TestDefaultProjects
{
  public string? StartupProject { get; set; }
  public string? BuildProject { get; set; }
  public string? TestProject { get; set; }
}

public sealed class TestSolutionSettings
{
  public TestDefaultProjects? Defaults { get; set; }
}
