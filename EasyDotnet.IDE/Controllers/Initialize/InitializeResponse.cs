namespace EasyDotnet.IDE.Controllers.Initialize;

public sealed record ServerInfo(string Name, string Version);

public sealed record InitializeResponse(ServerInfo ServerInfo, ServerCapabilities Capabilities, ToolPaths ToolPaths, BuildConfigurationInfo? BuildConfiguration);

public sealed record ServerCapabilities(List<string> Routes, List<string> ServerSentNotifications, bool SupportsSingleFileExecution);

public sealed record ToolPaths(string? MsBuildPath);

public sealed record BuildConfigurationInfo(string BuildType, string DisplayName);