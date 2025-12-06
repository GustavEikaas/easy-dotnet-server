namespace EasyDotnet.Domain.Models.NetcoreDbg;

public enum DebugSessionState
{
  Idle,
  Starting,
  Active,
  Stopping
}

public class DebugSession
{
  public required string DllPath { get; init; }
  public string? SessionId { get; init; }
  public DebugSessionState State { get; set; }
  public DateTime StartedAt { get; init; }
  public TaskCompletionSource<bool> CleanupComplete { get; } = new();
  public int? Port { get; set; }
}