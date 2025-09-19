
namespace EasyDotnet.Controllers.Initialize;

public sealed record InitializeRequest(ClientInfo ClientInfo, ProjectInfo ProjectInfo, Options? Options);

public sealed record ProjectInfo(string RootDir, string? SolutionFile);

public sealed record Options(DebuggerOptions? DebuggerOptions, bool UseVisualStudio = false);

public sealed record DebuggerOptions(string BinaryPath);

public sealed record ClientInfo(string Name, string? Version);