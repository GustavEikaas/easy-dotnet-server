namespace EasyDotnet.Domain.Models.Client;

public record RunCommand(
  string Executable,
  List<string> Arguments,
  string WorkingDirectory,
  Dictionary<string, string> EnvironmentVariables
);

public sealed record RunCommandResponse(int ProcessId);