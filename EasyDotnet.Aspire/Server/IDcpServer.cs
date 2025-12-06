namespace EasyDotnet.Aspire.Server;

public interface IDcpServer
{
  /// <summary>
  /// The port the DCP server is listening on (0 if not started)
  /// </summary>
  int Port { get; }

  /// <summary>
  /// The authentication token for DCP clients
  /// </summary>
  string Token { get; }

  /// <summary>
  /// Ensures the server is started (idempotent)
  /// </summary>
  Task EnsureStartedAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Sends a notification to a specific DCP instance via WebSocket
  /// </summary>
  Task SendNotificationAsync(string dcpId, object notification, CancellationToken cancellationToken);

  /// <summary>
  /// Checks if the server is currently running
  /// </summary>
  bool IsRunning { get; }
}
