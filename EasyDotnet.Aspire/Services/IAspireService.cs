using EasyDotnet.Aspire.Session;

namespace EasyDotnet.Aspire.Services;

public interface IAspireService
{
  /// <summary>
  /// Starts an Aspire debugging session for the specified AppHost project
  /// </summary>
  Task<AspireSession> StartAsync(
      string projectPath,
      CancellationToken cancellationToken = default);

  /// <summary>
  /// Stops an Aspire session
  /// </summary>
  Task StopAsync(
      string projectPath,
      CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the status of a session
  /// </summary>
  AspireSessionStatus? GetSessionStatus(string projectPath);
}

public enum AspireSessionStatus
{
  Starting,
  Running,
  Stopping,
  Stopped,
  Failed
}