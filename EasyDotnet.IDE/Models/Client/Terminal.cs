namespace EasyDotnet.IDE.Models.Client;

public sealed record TerminalOpenRequest(Guid JobId, string? SlotId, string Label, IReadOnlyList<string> Arguments);

public sealed record TerminalOpenResponse(int Rows, int Cols);

public sealed record TerminalOutputNotification(Guid JobId, byte[] Data);

public sealed record TerminalExitNotification(Guid JobId, int ExitCode);