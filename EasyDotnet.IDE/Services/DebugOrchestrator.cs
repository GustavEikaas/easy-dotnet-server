using System.Collections.Concurrent;
using System.Text.Json;
using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
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

      var session = debugSessionFactory.Create(
          async (dapRequest, proxy) =>
          {
            await strategy.TransformRequestAsync(dapRequest, proxy);
            return dapRequest;
          },
          applyValueConverters,
          variableLocationResolver);

      _sessionServices[sessionKey] = session;

      await strategy.PrepareAsync(cancellationToken);

      _ = Task.Run(async () =>
      {
        try
        {
          var proxy = await session.WaitForDebugSessionStartedAsync().WaitAsync(cancellationToken);
          await ProbeDebuggerProcessAsync(proxy, cancellationToken);
          if (debuggerEngine == DebuggerEngine.SharpDbg)
          {
            await WaitForSharpDbgAttachAsync(proxy, cancellationToken);
          }
          strategy.OnDebugSessionReady(session, proxy);
        }
        catch (OperationCanceledException) { }
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

  /// <summary>
  /// SharpDbg acks configurationDone before its deferred attach has completed, so the resume
  /// gate can be satisfied while nothing is attached and breakpoints are still pending.
  /// It reports an empty (but successful) threads list until the attach finishes, so poll
  /// until at least one thread is visible before declaring the session ready.
  /// </summary>
  private async Task WaitForSharpDbgAttachAsync(IDebuggerProxy proxy, CancellationToken cancellationToken)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));

    try
    {
      while (true)
      {
        var response = await proxy.RunInternalRequestAsync(new Request
        {
          Seq = 0,
          Type = "request",
          Command = "threads"
        }, timeout.Token);

        if (response.Success
            && response.Body is { ValueKind: JsonValueKind.Object } body
            && body.TryGetProperty("threads", out var threads)
            && threads.ValueKind == JsonValueKind.Array
            && threads.GetArrayLength() > 0)
        {
          logger.LogInformation("SharpDbg attach confirmed ({count} threads visible)", threads.GetArrayLength());
          return;
        }

        await Task.Delay(100, timeout.Token);
      }
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      throw new InvalidOperationException("SharpDbg did not complete its attach within 10 seconds; not resuming the debuggee");
    }
  }
}