namespace EasyDotnet.Domain.Models.Client;

public sealed record DebuggerOptions(string? BinaryPath = null, bool ApplyValueConverters = false, bool ExternalOutputWindow = false);
public sealed record ClientOptions(DebuggerOptions? DebuggerOptions = null, bool UseVisualStudio = false);
