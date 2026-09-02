using System.Collections.Concurrent;
using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Types;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public interface IDebugOrchestrator
{
  Task<Debugger.DebugSession> StartServerDebugSessionAsync(
      string sessionKey,
      string sessionId,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken);

  Task<Debugger.DebugSession> StartClientDebugSessionAsync(
      string sessionKey,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken);

  Debugger.DebugSession? GetSessionService(string sessionKey);

  Task StopDebugSessionAsync(string sessionKey);

  DebugSession? GetSession(string sessionKey);

  bool HasActiveSession(string sessionKey);
}

public class DebugOrchestrator(
    IDebugSessionManager debugSessionManager,
    IDebugSessionFactory debugSessionFactory,
    IEditorService editorService,
    IClientService clientService,
    IVariableLocationResolver variableLocationResolver,
    ILogger<DebugOrchestrator> logger) : IDebugOrchestrator
{
  private readonly ConcurrentDictionary<string, Debugger.DebugSession> _sessionServices = new();

  /// <summary>
  /// Budget for the whole DAP start handshake, from session start to the point we can resume the
  /// debuggee. The engines bound themselves well below this (netcoredbg waits 5s for the runtime's
  /// CreateProcess callback), so anything past it is a jam, not a slow machine. Without a bound, a
  /// debuggee suspended by the startup hook stays suspended for as long as the session is open.
  /// </summary>
  private static readonly TimeSpan DebugSessionStartTimeout = TimeSpan.FromSeconds(20);

  public async Task<Debugger.DebugSession> StartClientDebugSessionAsync(
      string sessionKey,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken)
  {
    logger.LogInformation("Starting debug session for {SessionKey}.", sessionKey);

    if (_sessionServices.TryGetValue(sessionKey, out var existingService))
    {
      if (!existingService.DisposalStarted.IsCompleted)
      {
        throw new InvalidOperationException($"A debug session is already in progress for {sessionKey}");
      }

      logger.LogInformation("Cleaning up previous session for {SessionKey}.", sessionKey);
      await existingService.ForceDisposeAsync();
      _sessionServices.TryRemove(sessionKey, out _);
    }

    return await debugSessionManager.StartClientSessionAsync(
        sessionKey,
        () => StartDebugSessionInternalAsync(sessionKey, strategy, cancellationToken),
        cancellationToken);
  }

  public async Task<Debugger.DebugSession> StartServerDebugSessionAsync(
      string sessionKey,
      string sessionId,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken)
  {
    logger.LogInformation("Starting server debug session for {SessionKey} (SessionId: {SessionId})", sessionKey, sessionId);

    if (_sessionServices.TryGetValue(sessionKey, out var existingService))
    {
      if (!existingService.DisposalStarted.IsCompleted)
      {
        throw new InvalidOperationException($"A debug session is already in progress for {sessionKey}");
      }

      logger.LogInformation("Existing session is disposing, forcing cleanup for {SessionKey}", sessionKey);
      await existingService.ForceDisposeAsync();
      _sessionServices.TryRemove(sessionKey, out _);
    }

    return await debugSessionManager.StartServerSessionAsync(
        sessionKey,
        sessionId,
        () => StartDebugSessionInternalAsync(sessionKey, strategy, cancellationToken),
        cancellationToken);
  }

  public async Task StopDebugSessionAsync(string sessionKey)
  {
    logger.LogInformation("Stopping debug session for {SessionKey}.", sessionKey);

    await debugSessionManager.EndSessionAsync(sessionKey, CancellationToken.None);

    if (_sessionServices.TryGetValue(sessionKey, out var service))
    {
      _ = Task.Run(async () =>
      {
        try
        {
          await service.DisposeAsync();
          logger.LogDebug("Background disposal complete for {SessionKey}.", sessionKey);
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Error during background disposal for {SessionKey}.", sessionKey);
        }
        finally
        {
          _sessionServices.TryRemove(sessionKey, out _);
        }
      });
    }
  }

  public DebugSession? GetSession(string sessionKey) =>
      debugSessionManager.GetSession(sessionKey);

  public bool HasActiveSession(string sessionKey) =>
      debugSessionManager.HasActiveSession(sessionKey);

  public Debugger.DebugSession? GetSessionService(string sessionKey)
  {
    _sessionServices.TryGetValue(sessionKey, out var service);
    return service;
  }

  private async Task<Debugger.DebugSession> StartDebugSessionInternalAsync(
      string sessionKey,
      IDebugSessionStrategy strategy,
      CancellationToken cancellationToken)
  {
    var label = Path.GetFileNameWithoutExtension(sessionKey);

    try
    {
      var binaryPath = clientService.ClientOptions?.DebuggerOptions?.BinaryPath;
      if (string.IsNullOrEmpty(binaryPath))
        throw new InvalidOperationException("Failed to start debugger, no binary path provided");

      var debuggerEngine = DebuggerLocator.GetConfiguredEngine(clientService.ClientOptions?.DebuggerOptions?.Engine);
      var (debuggerFileName, debuggerArguments) = DebuggerLocator.GetLaunchCommand(debuggerEngine, binaryPath);
      var applyValueConverters = debuggerEngine != DebuggerEngine.SharpDbg && (clientService?.ClientOptions?.DebuggerOptions?.ApplyValueConverters ?? false);
      var memCpuUsage = clientService?.ClientOptions?.DebuggerOptions?.MemCpuUsage ?? false;

      var session = debugSessionFactory.Create(
          async (dapRequest, proxy) =>
          {
            await strategy.TransformRequestAsync(dapRequest, proxy);
            return dapRequest;
          },
          applyValueConverters,
          memCpuUsage,
          variableLocationResolver);

      _sessionServices[sessionKey] = session;

      await strategy.PrepareAsync(cancellationToken);

      _ = Task.Run(async () =>
      {
        using var startBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startBudget.CancelAfter(DebugSessionStartTimeout);

        try
        {
          var proxy = await session.WaitForDebugSessionStartedAsync().WaitAsync(startBudget.Token);
          await ProbeDebuggerProcessAsync(proxy, startBudget.Token);
          strategy.OnDebugSessionReady(session, proxy);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
          await HandleStartTimeoutAsync(sessionKey, strategy);
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Failed to wait for DAP debug session start");
        }
      }, cancellationToken);

      try
      {
        session.Start(
            debuggerFileName,
            debuggerArguments,
            (ex) =>
            {
              editorService.DisplayError(ex.Message);
              logger.LogError(ex, "Failed to start debugger process for {Label}.", label);
            },
            async () =>
            {
              try
              {
                logger.LogDebug("Session cleanup callback invoked for {Label}.", label);
                await StopDebugSessionAsync(sessionKey);
              }
              catch (Exception ex)
              {
                logger.LogError(ex, "Error during session cleanup for {Label}.", label);
              }
              finally
              {
                await strategy.DisposeAsync();
              }
            },
            cancellationToken);

        logger.LogInformation("Debug session ready for {Label} on port {Port}.", label, session.Port);

        return session;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Failed to start debug session for {Label}.", label);

        await strategy.DisposeAsync();
        if (_sessionServices.TryRemove(sessionKey, out var service))
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
      logger.LogError(ex, "Error initializing debug session for {Label}.", label);
      await strategy.DisposeAsync();
      throw;
    }
  }

  /// <summary>
  /// The handshake never finished. Report it and tear the session down the same way a user-initiated
  /// stop does; <see cref="RunInTerminalStrategy"/> kills a still-suspended debuggee on disposal.
  /// </summary>
  private async Task HandleStartTimeoutAsync(string sessionKey, IDebugSessionStrategy strategy)
  {
    var seconds = (int)DebugSessionStartTimeout.TotalSeconds;

    logger.LogError(
      "Debug session for {SessionKey} did not finish starting within {Seconds}s. Tearing it down.",
      sessionKey,
      seconds);

    var message = strategy is RunInTerminalStrategy { RuntimeSuspended: true }
      ? "Failed to resume app, debugger failed to attach"
      : $"Debugger failed to start within {seconds}s";

    try
    {
      await editorService.DisplayError(message);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to report debug session start timeout to the editor.");
    }

    try
    {
      await StopDebugSessionAsync(sessionKey);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to stop debug session {SessionKey} after start timeout.", sessionKey);
    }
  }

  private static async Task ProbeDebuggerProcessAsync(IDebuggerProxy proxy, CancellationToken cancellationToken)
  {
    var response = await proxy.RunInternalRequestAsync(new Request
    {
      Seq = 0,
      Type = "request",
      Command = "threads"
    }, cancellationToken);

    if (!response.Success)
    {
      throw new InvalidOperationException($"Debugger process probe failed: {response.Message ?? "threads request failed"}");
    }
  }
}