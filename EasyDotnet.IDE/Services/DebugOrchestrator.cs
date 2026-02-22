using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.IDE.Controllers.NetCoreDbg;
using EasyDotnet.IDE.Types;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public interface IDebugOrchestrator
{
  Task<Debugger.DebugSession> StartServerDebugSessionAsync(
      string projectPath,
      string sessionId,
      DebuggerStartRequest startRequest,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken);

  Task<Debugger.DebugSession> StartClientDebugSessionAsync(
      string projectPath,
      DebuggerStartRequest startRequest,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken);

  Debugger.DebugSession? GetSessionService(string projectPath);

  Task StopDebugSessionAsync(string projectPath);

  DebugSession? GetSession(string projectPath);

  bool HasActiveSession(string projectPath);
}

public class DebugOrchestrator(
    IDebugSessionManager debugSessionManager,
    IDebugSessionFactory debugSessionFactory,
    IMsBuildService msBuildService,
    IEditorService editorService,
    IClientService clientService,
    ILogger<DebugOrchestrator> logger) : IDebugOrchestrator
{
  private readonly ConcurrentDictionary<string, Debugger.DebugSession> _sessionServices = new();

  public async Task<Debugger.DebugSession> StartClientDebugSessionAsync(
      string projectPath,
      DebuggerStartRequest startRequest,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken)
  {
    var projectName = Path.GetFileNameWithoutExtension(projectPath);
    logger.LogInformation("Starting debug session for {project}.", projectName);

    if (_sessionServices.TryGetValue(projectPath, out var existingService))
    {
      if (!existingService.DisposalStarted.IsCompleted)
      {
        throw new InvalidOperationException($"A debug session is already in progress for {projectName}");
      }

      logger.LogInformation("Cleaning up previous session for {project}.", projectName);
      await existingService.ForceDisposeAsync();
      _sessionServices.TryRemove(projectPath, out _);
    }

    return await debugSessionManager.StartClientSessionAsync(
        projectPath,
        () => StartDebugSessionInternalAsync(startRequest, strategy, cancellationToken),
        cancellationToken);
  }

  public async Task<Debugger.DebugSession> StartServerDebugSessionAsync(
      string projectPath,
      string sessionId,
      DebuggerStartRequest startRequest,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken)
  {
    logger.LogInformation("Starting server debug session for {projectPath} (SessionId: {sessionId})", projectPath, sessionId);

    if (_sessionServices.TryGetValue(projectPath, out var existingService))
    {
      if (!existingService.DisposalStarted.IsCompleted)
      {
        throw new InvalidOperationException($"A debug session is already in progress for {projectPath}");
      }

      logger.LogInformation("Existing session is disposing, forcing cleanup for {projectPath}", projectPath);
      await existingService.ForceDisposeAsync();
      _sessionServices.TryRemove(projectPath, out _);
    }

    return await debugSessionManager.StartServerSessionAsync(
        projectPath,
        sessionId,
        () => StartDebugSessionInternalAsync(startRequest, strategy, cancellationToken),
        cancellationToken);
  }

  public async Task StopDebugSessionAsync(string projectPath)
  {
    var projectName = Path.GetFileNameWithoutExtension(projectPath);
    logger.LogInformation("Stopping debug session for {project}.", projectName);

    await debugSessionManager.EndSessionAsync(projectPath, CancellationToken.None);

    if (_sessionServices.TryGetValue(projectPath, out var service))
    {
      _ = Task.Run(async () =>
      {
        try
        {
          await service.DisposeAsync();
          logger.LogDebug("Background disposal complete for {project}.", projectName);
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Error during background disposal for {project}.", projectName);
        }
        finally
        {
          _sessionServices.TryRemove(projectPath, out _);
        }
      });
    }
  }

  public DebugSession? GetSession(string projectPath) =>
      debugSessionManager.GetSession(projectPath);

  public bool HasActiveSession(string projectPath) =>
      debugSessionManager.HasActiveSession(projectPath);

  public Debugger.DebugSession? GetSessionService(string projectPath)
  {
    _sessionServices.TryGetValue(projectPath, out var service);
    return service;
  }

  private async Task<Debugger.DebugSession> StartDebugSessionInternalAsync(
      DebuggerStartRequest request,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken)
  {
    var projectPath = request.TargetPath;
    var projectName = Path.GetFileNameWithoutExtension(projectPath);

    try
    {
      var project = await msBuildService.GetOrSetProjectPropertiesAsync(
          request.TargetPath,
          request.TargetFramework,
          request.Configuration ?? "Debug",
          cancellationToken);

      var binaryPath = clientService.ClientOptions?.DebuggerOptions?.BinaryPath;
      if (string.IsNullOrEmpty(binaryPath))
      {
        throw new InvalidOperationException("Failed to start debugger, no binary path provided");
      }

      var platform = project.GetPlatform();
      if (platform != DotnetPlatform.None && platform != DotnetPlatform.Windows)
      {
        throw new InvalidOperationException($"Debugging for {platform} is not supported yet");

      }

      var session = debugSessionFactory.Create(async (dapRequest, proxy) =>
             {
               await strategy.TransformRequestAsync(dapRequest, proxy);
               return dapRequest;
             },
             clientService?.ClientOptions?.DebuggerOptions?.ApplyValueConverters ?? false
         );

      _sessionServices[projectPath] = session;

      await strategy.PrepareAsync(project, cancellationToken);

      _ = Task.Run(async () =>
      {
        try
        {
          var proxy = await session.WaitForConfigurationDoneAsync();
          // Giving the debugger 500ms of delay because this caused a race condition
          await Task.Delay(500, cancellationToken);
          strategy.OnDebugSessionReady(session, proxy);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Failed to wait for DAP configurationDone");
        }
      }, cancellationToken);

      try
      {
        session.Start(
            binaryPath,
            (ex) =>
            {
              editorService.DisplayError(ex.Message);
              logger.LogError(ex, "Failed to start debugger process for {project}.", projectName);
            },
            async () =>
            {
              try
              {
                logger.LogDebug("Session cleanup callback invoked for {project}.", projectName);
                await StopDebugSessionAsync(projectPath);
              }
              catch (Exception ex)
              {
                logger.LogError(ex, "Error during session cleanup for {project}.", projectName);
              }
              finally
              {
                await strategy.DisposeAsync();
              }
            },
            cancellationToken);

        logger.LogInformation("Debug session ready for {project} on port {port}.", projectName, session.Port);

        return session;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Failed to start debug session for {project}.", projectName);

        await strategy.DisposeAsync();
        if (_sessionServices.TryRemove(projectPath, out var service))
        {
          try
          {
            await service.DisposeAsync();
          }
          catch (Exception disposeEx)
          {
            logger.LogWarning(disposeEx, "Error disposing service after failure.");
          }
        }
        throw;
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error initializing debug session for {project}.", projectName);
      await strategy.DisposeAsync();
      throw;
    }
  }
}