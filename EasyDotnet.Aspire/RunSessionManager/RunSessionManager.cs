using System.Collections.Concurrent;
using EasyDotnet.Aspire.Contracts;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Aspire.RunSessionManager;

public sealed class DcpRunSessionException(string code, string message) : Exception(message)
{
  public string Code { get; } = code;
}

public sealed class RunSessionManager(IIdeCallback ide, ILogger<RunSessionManager> logger)
{
  private const int LaunchFailureExitCode = 1;

  private readonly ConcurrentDictionary<string, byte> _sessions = new();

  public INotificationSink? Notifications { get; set; }

  public string CreateRunSession(RunSessionPayload payload)
  {
    var config = ValidateAndGetConfig(payload);

    var runId = Guid.NewGuid().ToString("N");
    var request = BuildRequest(runId, config, payload);

    _sessions[runId] = 0;
    _ = Task.Run(() => RunAsync(runId, request), CancellationToken.None);

    var aspnetUrls = request.EnvironmentVariables.TryGetValue("ASPNETCORE_URLS", out var urls) ? urls : "<unset>";
    logger.LogInformation("Created run session {RunId} for {Project} (mode={Mode}, debug={Debug}, ASPNETCORE_URLS={Urls})",
        runId, config.ProjectPath, config.Mode, request.Debug, aspnetUrls);
    return runId;
  }

  public async Task ReportProcessIdAsync(string runId, int pid)
  {
    if (pid <= 0 || !_sessions.ContainsKey(runId))
    {
      return;
    }
    var sink = Notifications;
    if (sink is not null)
    {
      logger.LogInformation("Notifying DCP processRestarted: run {RunId} pid {Pid}", runId, pid);
      await sink.SendAsync(new ProcessRestartedNotification(runId, (uint)pid), CancellationToken.None);
    }
  }

  public async Task<bool> StopRunSessionAsync(string runId, CancellationToken ct)
  {
    if (!_sessions.ContainsKey(runId))
    {
      return false;
    }

    await ide.StopManagedResourceAsync(runId, ct);
    return true;
  }

  /// <summary>
  /// Terminates every tracked run session. Called when the AppHost exits — its child
  /// resources are managed processes that would otherwise be orphaned. Each child's own
  /// <see cref="RunAsync"/> removes it from <see cref="_sessions"/> as it dies, so this is
  /// best-effort: failures are logged, never thrown, so teardown always completes.
  /// </summary>
  public Task StopAllAsync(CancellationToken ct) =>
      Task.WhenAll(_sessions.Keys.Select(async runId =>
      {
        try
        {
          await ide.StopManagedResourceAsync(runId, ct);
        }
        catch (Exception ex)
        {
          logger.LogWarning(ex, "Failed to stop run session {RunId} during shutdown", runId);
        }
      }));

  private async Task RunAsync(string runId, RunManagedResourceRequest request)
  {
    int? exitCode = null;
    try
    {
      exitCode = await ide.RunManagedResourceAsync(request, CancellationToken.None);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Run session {RunId} failed to launch", runId);
      exitCode = LaunchFailureExitCode;
    }
    finally
    {
      _sessions.TryRemove(runId, out _);
      var sink = Notifications;
      if (sink is not null)
      {
        logger.LogInformation("Notifying DCP sessionTerminated: run {RunId} exit {ExitCode}", runId, exitCode);
        await sink.SendAsync(new SessionTerminatedNotification(runId, exitCode), CancellationToken.None);
      }
    }
  }

  private static LaunchConfiguration ValidateAndGetConfig(RunSessionPayload payload)
  {
    var config = payload.LaunchConfigurations?.FirstOrDefault()
        ?? throw new DcpRunSessionException("NoLaunchConfiguration", "run_session payload had no launch configurations.");

    if (!string.Equals(config.Type, LaunchConfigurationTypes.Project, StringComparison.Ordinal))
    {
      throw new DcpRunSessionException(
          "UnsupportedLaunchConfiguration",
          $"Unsupported launch configuration type '{config.Type}'. This IDE endpoint only supports '{LaunchConfigurationTypes.Project}'.");
    }

    if (string.IsNullOrEmpty(config.ProjectPath))
    {
      throw new DcpRunSessionException("ProjectNotFound", "Project launch configuration is missing 'project_path'.");
    }

    return config;
  }

  private static RunManagedResourceRequest BuildRequest(string runId, LaunchConfiguration config, RunSessionPayload payload)
  {
    var projectPath = config.ProjectPath!;

    var env = new Dictionary<string, string>();
    foreach (var e in payload.Env ?? [])
    {
      env[e.Name] = e.Value ?? string.Empty;
    }

    var debug = string.Equals(config.Mode, LaunchModes.Debug, StringComparison.OrdinalIgnoreCase);
    return new RunManagedResourceRequest(runId, projectPath, payload.Args, env, debug);
  }
}