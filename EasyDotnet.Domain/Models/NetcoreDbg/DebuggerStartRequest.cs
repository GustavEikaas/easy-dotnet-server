namespace EasyDotnet.Domain.Models.NetcoreDbg;

public record EnvironmentVariable(string Name, string Value);

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName,
  EnvironmentVariable[]? EnvironmentVariables = null
);