using System.Diagnostics;

namespace EasyDotnet.Aspire.Session;

public class RunSession
{
  public required string RunId { get; init; }
  public required string DcpId { get; init; }
  public required string ProjectPath { get; init; }
  public int? DebuggerPort { get; init; }
  public int? DebugSessionId { get; init; }
  public bool IsDebug { get; init; }
  public Process? ServiceProcess { get; init; }
  public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public static class RunSessionExtensions
{
  public static string GenerateRunId() => $"run-{Guid.NewGuid():N}";
}