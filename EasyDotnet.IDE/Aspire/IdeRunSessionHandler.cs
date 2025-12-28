using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Aspire.Models;
using EasyDotnet.Aspire.Server;
using EasyDotnet.Aspire.Server.Handlers;
using EasyDotnet.Aspire.Session;
using EasyDotnet.Domain.Models.NetcoreDbg;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Aspire;

/// <summary>
/// IDE-specific implementation of run session handler that orchestrates debugging
/// </summary>
public class IdeRunSessionHandler(
    IDebugOrchestrator debugOrchestrator,
    IClientService clientService,
    IMsBuildService msBuildService,
    IAspireSessionManager sessionManager,
    IDcpServer dcpServer,
    ILogger<IdeRunSessionHandler> logger) : IRunSessionHandler
{
  public async Task<RunSession> HandleCreateAsync(
      string dcpId,
      LaunchConfigurationDto config,
      EnvVar[] envVars,
      CancellationToken cancellationToken)
  {

    var x = envVars;
    var runId = RunSessionExtensions.GenerateRunId();

    logger.LogInformation(
        "Creating run session {RunId} for project {ProjectPath} with DCP ID {DcpId}",
        runId,
        config.ProjectPath,
        dcpId);

    // Look up the session by DCP ID (already set during auth)
    var aspireSession = sessionManager.GetSessionByDcpId(dcpId);

    if (aspireSession == null)
    {
      logger.LogError("No Aspire session found for DCP ID: {DcpId}", dcpId);
      throw new InvalidOperationException(
          $"No Aspire session found for DCP ID: {dcpId}. This should not happen if auth worked.");
    }

    // Get or load project properties
    var project = await msBuildService.GetOrSetProjectPropertiesAsync(
        config.ProjectPath!,
        null,
        "Debug",
        cancellationToken);

    // Determine if debugging is enabled
    var isDebug = config.Mode != "NoDebug";

    // if (!isDebug)
    // {
    //   throw new InvalidOperationException("Non-debug mode is not currently supported");
    // }

    try
    {
      // Start the debug session through the orchestrator
      var debugSession = await debugOrchestrator.StartClientDebugSessionAsync(
          config.ProjectPath!,
          new(project.ProjectPath!, project.TargetFramework, null, null, [.. envVars.Select(x => new EnvironmentVariable(x.Name, x.Value))]),
          cancellationToken);

      logger.LogInformation(
          "Started debugger on port {Port} for {ProjectPath}",
          debugSession.Port,
          config.ProjectPath);

      // Connect to the debugger through client service
      var debugSessionId = await clientService.RequestStartDebugSession(
          "127.0.0.1",
          debugSession.Port);

      logger.LogInformation(
          "Connected to debug session {DebugSessionId} for {ProjectPath}",
          debugSessionId,
          config.ProjectPath);

      await debugSession.ProcessStarted;

      // Create the run session
      var runSession = new RunSession
      {
        RunId = runId,
        DcpId = dcpId,
        ProjectPath = config.ProjectPath!,
        DebuggerPort = debugSession.Port,
        DebugSessionId = debugSessionId,
        IsDebug = isDebug,
        ProcessId = debugSession.ProcessId!.Value
      };

      // Register with session manager
      sessionManager.AddRunSession(dcpId, runSession);

      logger.LogInformation(
          "Run session {RunId} created successfully for {ProjectPath}",
          runId,
          config.ProjectPath);

      return runSession;
    }
    catch (Exception ex)
    {
      logger.LogError(
          ex,
          "Failed to create run session {RunId} for {ProjectPath}",
          runId,
          config.ProjectPath);
      throw;
    }
  }

  public async Task HandleTerminateAsync(
      string runId,
      CancellationToken cancellationToken = default)
  {
    logger.LogInformation("Terminating run session {RunId}", runId);

    var runSession = sessionManager.GetRunSession(runId) ?? throw new KeyNotFoundException($"Run session not found: {runId}");
    try
    {
      // Terminate the debug session if it exists
      if (runSession.DebugSessionId.HasValue)
      {
        await debugOrchestrator.StopDebugSessionAsync(runSession.ProjectPath);
        await clientService.RequestTerminateDebugSession(runSession.DebugSessionId.Value);
        logger.LogInformation(
            "Debug session {DebugSessionId} terminated for run session {RunId}",
            runSession.DebugSessionId.Value,
            runId);
      }

      // Send termination notification to DCP
      await dcpServer.SendNotificationAsync(runSession.DcpId, new
      {
        notification_type = "sessionTerminated",
        session_id = runId,
        exit_code = 0 //TODO:
      }, cancellationToken);

      // Remove from session manager
      sessionManager.RemoveRunSession(runId);

      logger.LogInformation("Run session {RunId} terminated successfully", runId);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error terminating run session {RunId}", runId);
      throw;
    }
  }
}