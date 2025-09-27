namespace EasyDotnet.Domain.Models.Client;

public sealed record DebuggerOptions(string? BinaryPath = null);
public sealed record ClientOptions(DebuggerOptions? DebuggerOptions = null, bool UseVisualStudio = false, bool UseRoslynLsp = false);