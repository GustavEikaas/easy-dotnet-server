using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.IDE.Controllers.NetCoreDbg;
using EasyDotnet.IDE.OutputWindow;
using EasyDotnet.Infrastructure.Dap;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public interface IDebugOrchestrator
{
  Task<int> StartServerDebugSessionAsync(
    string dllPath,
    string sessionId,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Task<int> StartClientDebugSessionAsync(
    string dllPath,
    DebuggerStartRequest request,
    CancellationToken cancellationToken);

  Debugger.DebugSession? GetSessionService(string dllPath);

  Task StopDebugSessionAsync(string dllPath);

  DebugSession? GetSession(string dllPath);

  bool HasActiveSession(string dllPath);
}

public class DebugOrchestrator(
  IDebugSessionManager debugSessionManager,
  IDebugSessionFactory debugSessionFactory,
  IMsBuildService msBuildService,
  ILaunchProfileService launchProfileService,
  INotificationService notificationService,
  IClientService clientService,
  IOutputWindowManager outputWindowManager,
  ILogger<DebugOrchestrator> logger) : IDebugOrchestrator
{
  private readonly ConcurrentDictionary<string, Debugger.DebugSession> _sessionServices = new();

  public async Task<int> StartClientDebugSessionAsync(
    string dllPath,
    DebuggerStartRequest request,
    CancellationToken cancellationToken)
  {
    var projectName = Path.GetFileNameWithoutExtension(dllPath);
    logger.LogInformation("Starting debug session for {project}.", projectName);

    if (_sessionServices.TryGetValue(dllPath, out var existingService))
    {
      if (!existingService.DisposalStarted.IsCompleted)
      {
        throw new InvalidOperationException($"A debug session is already in progress for {projectName}");
      }

      logger.LogInformation("Cleaning up previous session for {project}.", projectName);
      await existingService.ForceDisposeAsync();
      _sessionServices.TryRemove(dllPath, out _);
    }

    return await debugSessionManager.StartClientSessionAsync(
        dllPath,
        () => StartDebugSessionInternalAsync(request, cancellationToken),
        cancellationToken);
  }

  public async Task<int> StartServerDebugSessionAsync(
    string dllPath,
    string sessionId,
    DebuggerStartRequest request,
    CancellationToken cancellationToken)
  {
    logger.LogInformation("Starting server debug session for {dllPath} (SessionId: {sessionId})", dllPath, sessionId);

    if (_sessionServices.TryGetValue(dllPath, out var existingService))
    {
      if (!existingService.DisposalStarted.IsCompleted)
      {
        throw new InvalidOperationException($"A debug session is already in progress for {dllPath}");
      }

      logger.LogInformation("Existing session is disposing, forcing cleanup for {dllPath}", dllPath);
      await existingService.ForceDisposeAsync();
      _sessionServices.TryRemove(dllPath, out _);
    }

    return await debugSessionManager.StartServerSessionAsync(
      dllPath,
      sessionId,
      () => StartDebugSessionInternalAsync(request, cancellationToken),
      cancellationToken);
  }

  public async Task StopDebugSessionAsync(string dllPath)
  {
    var projectName = Path.GetFileNameWithoutExtension(dllPath);
    logger.LogInformation("Stopping debug session for {project}.", projectName);

    await debugSessionManager.EndSessionAsync(dllPath, CancellationToken.None);

    if (_sessionServices.TryGetValue(dllPath, out var service))
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
          _sessionServices.TryRemove(dllPath, out _);
        }
      });
    }
  }

  public DebugSession? GetSession(string dllPath) =>
    debugSessionManager.GetSession(dllPath);

  public bool HasActiveSession(string dllPath) =>
    debugSessionManager.HasActiveSession(dllPath);

  public Debugger.DebugSession? GetSessionService(string dllPath)
  {
    _sessionServices.TryGetValue(dllPath, out var service);
    return service;
  }

  private async Task<int> StartDebugSessionInternalAsync(
    DebuggerStartRequest request,
    CancellationToken cancellationToken)
  {
    var dllPath = request.TargetPath;
    var projectName = Path.GetFileNameWithoutExtension(dllPath);

    try
    {
      var project = await msBuildService.GetOrSetProjectPropertiesAsync(
          request.TargetPath,
          request.TargetFramework,
          request.Configuration ?? "Debug",
          cancellationToken);

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

      // START OUTPUT WINDOW IF REQUESTED
      string? outputWindowPipe = null;
      Action<DebugOutputEvent>? outputHandler = null;
      var externalOutputWindow = clientService.ClientOptions?.DebuggerOptions?.ExternalOutputWindow;

      if (externalOutputWindow == true)
      {
        try
        {
          logger.LogInformation("Starting external output window for {project}", projectName);
          outputWindowPipe = await outputWindowManager.StartOutputWindowAsync(dllPath, cancellationToken);

          // Store pipe name in session
          var managedSession = debugSessionManager.GetSession(dllPath);
          if (managedSession != null)
          {
            managedSession.OutputWindowPipeName = outputWindowPipe;
          }

          // Create output handler callback
          outputHandler = (output) => _ = Task.Run(async () =>
                  {
                    try
                    {
                      await outputWindowManager.SendOutputAsync(dllPath, output);
                    }
                    catch (Exception ex)
                    {
                      logger.LogWarning(ex, "Failed to forward output to external window");
                    }
                  });
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Failed to start external output window for {project}, continuing without it", projectName);
        }
      }

      var vsTestResult = StartVsTestIfApplicable(project, request.TargetPath);

      // Pass output handler to factory - it will suppress output events if handler is provided
      var debugSession = debugSessionFactory.Create(
          async (attachRequest) =>
              await InitializeRequestRewriter.CreateInitRequestBasedOnProjectType(
                  project,
                  launchProfile,
                  attachRequest,
                  project.ProjectDir!,
                  vsTestResult?.Item2),
          clientService?.ClientOptions?.DebuggerOptions?.ApplyValueConverters ?? false,
          outputHandler  // Pass the handler here
      );

      _sessionServices[dllPath] = debugSession;

      try
      {
        debugSession.Start(
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

               // CLEANUP OUTPUT WINDOW
               if (outputWindowPipe != null)
               {
                 await outputWindowManager.StopOutputWindowAsync(dllPath);
               }

               await StopDebugSessionAsync(dllPath);
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

        logger.LogInformation("Debug session ready for {project} on port {port}.", projectName, debugSession.Port);

        return debugSession.Port;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Failed to start debug session for {project}.", projectName);

        // CLEANUP OUTPUT WINDOW ON FAILURE
        if (outputWindowPipe != null)
        {
          await outputWindowManager.StopOutputWindowAsync(dllPath);
        }

        if (_sessionServices.TryRemove(dllPath, out var service))
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
