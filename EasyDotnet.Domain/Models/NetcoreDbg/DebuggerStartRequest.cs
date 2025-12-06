namespace EasyDotnet.Domain.Models.NetcoreDbg;

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName
);