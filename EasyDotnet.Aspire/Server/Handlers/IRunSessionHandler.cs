using EasyDotnet.Aspire.Models;
using EasyDotnet.Aspire.Session;

namespace EasyDotnet.Aspire.Server.Handlers;

public interface IRunSessionHandler
{
  /// <summary>
  /// Handles the creation of a new run session (starts debugging for a service)
  /// </summary>
  Task<RunSession> HandleCreateAsync(
      string dcpId,
      LaunchConfigurationDto config,
      EnvVar[] envVars,
      CancellationToken cancellationToken = default);

  /// <summary>
  /// Handles the termination of a run session (stops debugging)
  /// </summary>
  Task HandleTerminateAsync(
      string runId,
      CancellationToken cancellationToken = default);
}