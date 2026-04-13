namespace EasyDotnet.ContainerTests;

public sealed record TestServerInfo(string Name, string Version);
public sealed record TestServerCapabilities(List<string> Routes, List<string> ServerSentNotifications);
public sealed record TestInitializeResponse(TestServerInfo ServerInfo, TestServerCapabilities Capabilities);
public sealed record TestInitializeRequest(TestClientInfo ClientInfo, TestProjectInfo ProjectInfo);
public sealed record TestProjectInfo(string RootDir, string? SolutionFile = null);
public sealed record TestClientInfo(string Name, string? Version);
