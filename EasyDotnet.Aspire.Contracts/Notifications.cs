using System.Text.Json.Serialization;

namespace EasyDotnet.Aspire.Contracts;

/// <summary>Notification type discriminators for the WS notify stream.</summary>
public static class NotificationTypes
{
  public const string ProcessRestarted = "processRestarted";
  public const string SessionTerminated = "sessionTerminated";
  public const string ServiceLogs = "serviceLogs";
  public const string SessionMessage = "sessionMessage";
}

/// <summary>
/// host -&gt; DCP notification emitted when the resource process (re)starts.
/// May be omitted if the PID is unknown.
/// </summary>
public sealed record ProcessRestartedNotification(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("pid")] uint Pid)
{
  [JsonPropertyName("notification_type")]
  public string NotificationType => NotificationTypes.ProcessRestarted;
}

/// <summary>
/// host -&gt; DCP notification emitted when the run session ends.
/// <c>exit_code</c> may be omitted if it could not be captured.
/// </summary>
public sealed record SessionTerminatedNotification(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("exit_code")] int? ExitCode)
{
  [JsonPropertyName("notification_type")]
  public string NotificationType => NotificationTypes.SessionTerminated;
}