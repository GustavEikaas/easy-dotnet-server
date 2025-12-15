using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.IDE.Controllers.NetCoreDbg;
using EasyDotnet.Infrastructure.Dap;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public interface IDebugOrchestrator
{
  Task<Debugger.DebugSession> StartServerDebugSessionAsync(
    string projectPath,
    string sessionId,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Task<Debugger.DebugSession> StartClientDebugSessionAsync(
    string projectPath,
    DebuggerStartRequest request,
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
  ILaunchProfileService launchProfileService,
  INotificationService notificationService,
  IClientService clientService,
  ILogger<DebugOrchestrator> logger) : IDebugOrchestrator
{
  private readonly ConcurrentDictionary<string, Debugger.DebugSession> _sessionServices = new();

  public async Task<Debugger.DebugSession> StartClientDebugSessionAsync(
    string projectPath,
    DebuggerStartRequest request,
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
        () => StartDebugSessionInternalAsync(request, cancellationToken),
        cancellationToken);
  }

  public async Task<Debugger.DebugSession> StartServerDebugSessionAsync(
    string projectPath,
    string sessionId,
    DebuggerStartRequest request,
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
      () => StartDebugSessionInternalAsync(request, cancellationToken),
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

      var platform = project.GetPlatform();

      if (platform != DotnetPlatform.None)
      {
        throw new InvalidOperationException($"Debugging for {platform} is not supported yet");
      }

      var launchProfile = !string.IsNullOrEmpty(request.LaunchProfileName)
          ? (launchProfileService.GetLaunchProfiles(request.TargetPath)
             is { } profiles && profiles.TryGetValue(request.LaunchProfileName, out var profile)
                ? profile
                : null)
          : null;

      var binaryPath = clientService.ClientOptions?.DebuggerOptions?.BinaryPath;
      if (string.IsNullOrEmpty(binaryPath))
      {
        throw new InvalidOperationException("Failed to start debugger, no binary path provided");
      }

      var vsTestResult = StartVsTestIfApplicable(project, request.TargetPath);

      var session = debugSessionFactory.Create(async (attachRequest) =>
                await InitializeRequestRewriter.CreateInitRequestBasedOnProjectType(
                    project,
                    launchProfile,
                    attachRequest,
                    project.ProjectDir!,
                    vsTestResult?.Item2),
            clientService?.ClientOptions?.DebuggerOptions?.ApplyValueConverters ?? false
      );
      _sessionServices[projectPath] = session;

      try
      {
        session.Start(
           binaryPath,
           (ex) =>
           {
             notificationService.DisplayError(ex.Message);
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
               CleanupVsTest(vsTestResult);
             }
           }, cancellationToken);

        logger.LogInformation("Debug session ready for {project} on port {port}.", projectName, session.Port);

        return session;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Failed to start debug session for {project}.", projectName);

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

        CleanupVsTest(vsTestResult);
        throw;
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error initializing debug session for {project}.", projectName);
      throw;
    }
  }

  private static (Process, int)? StartVsTestIfApplicable(DotnetProject project, string projectPath) => project.IsTestProject && !project.TestingPlatformDotnetTestSupport
      ? VsTestHelper.StartTestProcess(projectPath)
      : null;

  private void CleanupVsTest((Process, int)? vsTestResult)
  {
    if (vsTestResult is { } value)
    {
      var (process, pid) = value;
      SafeDisposeProcess(process, "VsTest");
      SafeDisposeProcessById(pid, "VsTestHost");
    }
  }

  private void SafeDisposeProcessById(int pid, string processName)
  {
    try
    {
      var process = Process.GetProcessById(pid);
      SafeDisposeProcess(process, $"{processName} (PID: {pid})");
    }
    catch (ArgumentException)
    {
      logger.LogInformation("{processName} (PID: {pid}) not found", processName, pid);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to get {processName} process by PID: {pid}", processName, pid);
    }
  }

  private void SafeDisposeProcess(Process? process, string processName)
  {
    if (process == null) return;

    try
    {
      if (!process.HasExited)
      {
        process.Kill();
        logger.LogInformation("Killed {processName} process", processName);
      }
      else
      {
        logger.LogDebug("{processName} process already exited", processName);
      }
    }
    catch (InvalidOperationException)
    {
      logger.LogDebug("{processName} process already exited", processName);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to kill {processName} process", processName);
    }
    finally
    {
      try
      {
        process.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to dispose {processName} process", processName);
      }
    }
  }

}