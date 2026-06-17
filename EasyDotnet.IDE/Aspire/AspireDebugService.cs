using System.Collections.Concurrent;
using EasyDotnet.Aspire.Contracts;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Workspace.Services;
using Microsoft.Extensions.Logging;
using DebugSession = EasyDotnet.Debugger.DebugSession;

namespace EasyDotnet.IDE.Aspire;

/// <summary>
/// Debugs a DCP resource (mode=Debug) on behalf of the spawned Aspire host, reusing the same
/// machinery as <c>WorkspaceService.StartDebugSessionAsync</c>: launch the project under netcoredbg
/// (with DCP's env injected), tell Neovim to attach via <see cref="IEditorService.RequestStartDebugSession"/>,
/// then block until the debug session ends and report the exit code. Mirrors <see cref="AspireRunService"/> for the Debug path
/// </summary>
public sealed class AspireDebugService(
    WorkspaceBuildHostManager buildHostManager,
    WorkspaceSessionRegistry sessionRegistry,
    INotificationService notificationService,
    IEditorService editorService,
    IDebugOrchestrator debugOrchestrator,
    IDebugStrategyFactory debugStrategyFactory,
    ILogger<AspireDebugService> logger)
{
  private readonly ConcurrentDictionary<string, DebugSession> _sessions = new();
  private readonly ConcurrentDictionary<string, int> _sessionIds = new();

  public async Task<int> DebugAsync(RunManagedResourceRequest request, Func<int, Task> reportPid, CancellationToken ct)
  {
    var project = await ResolveProjectAsync(request.ProjectPath, ct) ?? throw new InvalidOperationException($"Could not evaluate project '{request.ProjectPath}' for debug");

    logger.LogInformation("Debugging Aspire resource {RunId} from {Project} ({Tfm})", request.RunId, project.ProjectName, project.TargetFramework);

    var sessionKey = SessionKey(request.RunId);
    var parentKey = SessionKey(AspireRunIds.AppHost);
    sessionRegistry.TryClaim(sessionKey, project.ProjectName, isDebug: true, parentKey: parentKey);
    await NotifyRunningSessionsAsync();

    DebugSession? session = null;
    try
    {
      var cliArgs = request.Args is { Count: > 0 } ? string.Join(' ', request.Args) : null;
      var strategy = debugStrategyFactory.CreateRunInTerminalStrategy(project, launchProfileName: null, cliArgs: cliArgs, extraEnv: request.EnvironmentVariables);

      session = await debugOrchestrator.StartClientDebugSessionAsync(project.ProjectFullPath, strategy, ct);
      _sessions[request.RunId] = session;

      _sessionIds[request.RunId] = await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
      await session.WaitForDebugSessionStartedAsync().WaitAsync(ct);

      _ = session.DebugeeProcessStarted.ContinueWith(_ =>
      {
        if (session.DebugeeProcessId is int pid && pid > 0)
        {
          sessionRegistry.SetProcessInfo(sessionKey, new RunningProcessEntry(sessionKey, project.ProjectName, project.ProjectFullPath, project.TargetFramework, pid, parentKey));
          _ = NotifyRunningSessionsAsync();
          reportPid(pid);
        }
      }, TaskScheduler.Default);

      await session.DisposalStarted;
      return session.ExitCode ?? 0;
    }
    finally
    {
      _sessions.TryRemove(request.RunId, out _);
      _sessionIds.TryRemove(request.RunId, out _);
      sessionRegistry.Release(sessionKey);
      await NotifyRunningSessionsAsync();
    }
  }

  /// <summary>
  /// Terminates the debug session backing <paramref name="runId"/> (DCP <c>DELETE /run_session</c>).
  /// </summary>
  public async Task StopAsync(string runId)
  {
    if (_sessionIds.TryGetValue(runId, out var sessionId))
    {
      try
      {
        await editorService.RequestTerminateDebugSession(sessionId);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to terminate debug session for {RunId}", runId);
      }
    }

    if (_sessions.TryGetValue(runId, out var session))
    {
      await session.ForceDisposeAsync();
    }
  }

  public bool Owns(string runId) => _sessions.ContainsKey(runId);

  private static string SessionKey(string runId) => $"aspire:{runId}";

  private Task NotifyRunningSessionsAsync() =>
      notificationService.NotifyRunningProcessesChangedAsync(
          [.. sessionRegistry.GetAllRunningSessions().Select(s => new RunningSessionInfo(s.ProjectName, s.IsDebugging))]);

  private async Task<ValidatedDotnetProject?> ResolveProjectAsync(string projectPath, CancellationToken ct)
  {
    var request = new GetProjectPropertiesBatchRequest([projectPath], Configuration: null, Platform: null, ComputeRunArguments: true);
    await foreach (var result in buildHostManager.GetProjectPropertiesBatchAsync(request, ct))
    {
      if (result is { Success: true, Project: not null })
      {
        return result.Project;
      }
    }
    return null;
  }
}