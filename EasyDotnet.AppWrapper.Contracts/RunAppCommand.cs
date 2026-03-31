namespace EasyDotnet.AppWrapper.Contracts;

public sealed record RunAppCommand(
    Guid JobId,
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    Dictionary<string, string> EnvironmentVariables
);
