using System.Collections.Generic;

namespace EasyDotnet.Controllers.Initialize;

public sealed record ServerInfo(string Name, string Version);

public sealed record InitializeResponse(ServerInfo ServerInfo, ServerCapabilities Capabilities, ToolPaths ToolPaths);

public sealed record ServerCapabilities(List<string> Routes, List<string> ServerSentNotifications, bool SupportsSingleFileExecution);

public sealed record ToolPaths(string? MsBuildPath);