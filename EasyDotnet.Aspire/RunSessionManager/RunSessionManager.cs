using System.Collections.Concurrent;
using EasyDotnet.Aspire.Contracts;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Aspire.RunSessionManager;

/// <summary>
/// A run_session request that is invalid/unsupported, carrying a machine-readable
/// <see cref="Code"/> for the DCP error response (spec §"Error reporting").
/// </summary>
public sealed class DcpRunSessionException(string code, string message) : Exception(message)
{
  public string Code { get; } = code;
}

/// <summary>
/// Owns the runId &lt;-&gt; managed-job mapping. For each <c>PUT /run_session</c> it
/// asks the IDE to run the resource (blocking call) and, when that completes,
/// emits a <c>sessionTerminated</c> notification carrying the exit code.
/// </summary>
public sealed class RunSessionManager(IIdeCallback ide, ILogger<RunSessionManager> logger)
{
  // Non-zero exit code reported when a resource fails to launch, so DCP/dashboard shows a
  // failure rather than a clean exit (the baseline protocol has no richer failure channel).
  private const int LaunchFailureExitCode = 1;

  // Set of active run-session ids (value is unused).
  private readonly ConcurrentDictionary<string, byte> _sessions = new();

  /// <summary>Notification sink, set by the DCP server once its WS is wired up.</summary>
  public INotificationSink? Notifications { get; set; }

  /// <summary>
  /// Starts a run session for the first project launch configuration in the
  /// payload and returns its runId immediately (DCP expects a prompt 201).
  /// </summary>
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

  /// <summary>
  /// Reports the OS pid for a run session as a <c>processRestarted</c> notification to DCP.
  /// No-op for unknown runIds (e.g. the AppHost, which is not a DCP run session) or pid 0.
  /// </summary>
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

  /// <summary>
  /// Stops a run session (DCP <c>DELETE /run_session</c>). Returns false if the runId is
  /// unknown. Kills the resource via the IDE; the in-flight run worker then completes
  /// naturally and emits <c>sessionTerminated</c>.
  /// </summary>
  public async Task<bool> StopRunSessionAsync(string runId, CancellationToken ct)
  {
    if (!_sessions.ContainsKey(runId))
    {
      return false;
    }

    await ide.StopManagedResourceAsync(runId, ct);
    return true;
  }

  private async Task RunAsync(string runId, RunManagedResourceRequest request)
  {
    // A normal exit (including a user stop, which kills the process) returns the real exit code;
    // an exception means it never ran (failed to resolve/launch) -> report a non-zero exit so the
    // dashboard shows a failure rather than a clean/unknown one.
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

  // Validates the request synchronously so a malformed/unsupported run_session fails the PUT
  // with a proper error (rather than 201-then-silent-failure).
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

  // The Aspire host only relays DCP's launch-config essentials. The IDE resolves
  // the real run command (executable, CWD, DOTNET_ROOT, etc.) from the project.
  // DCP's env[] is already fully resolved (it folds in launch-profile values and
  // carries the ASPNETCORE_URLS the resource must bind), so it is forwarded verbatim.
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