using System.Text.Json;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class DebuggerMessageInterceptor(
  ILogger<DebuggerMessageInterceptor> logger,
  ValueConverterService valueConverterService,
  bool applyValueConverters,
  Action<int> onDebugeeProcessStarted,
  FrameSourceTracker? frameSourceTracker = null,
  IVariableLocationResolver? variableLocationResolver = null) : IDapMessageInterceptor

{
  public async Task<ProtocolMessage?> InterceptAsync(
    ProtocolMessage message,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    try
    {
      return await (message switch
      {
        VariablesResponse varRes => HandleVariablesResponse(varRes),
        Response res => HandleResponse(res),
        Event evt => HandleEvent(evt),
        _ => throw new Exception($"Unsupported DAP message from debugger: {message}")
      });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Exception in debugger DAP handler");
      throw;
    }
  }

  private Task<ProtocolMessage?> HandleVariablesResponse(VariablesResponse response)
  {
    logger.LogDebug("[DEBUGGER] Variables response");

    valueConverterService.FormatInlineStringJsonVariables(response);

    if (applyValueConverters)
    {
      valueConverterService.RegisterVariablesReferences(response);
    }

    TryDecorateVariablesWithLocations(response);

    return Task.FromResult<ProtocolMessage?>(response);
  }

  private Task<ProtocolMessage?> HandleResponse(Response response)
  {
    logger.LogDebug("[DEBUGGER] Response: {command}", response.Command);

    if (response.Command == "stackTrace")
    {
      TryCaptureStackTrace(response);
    }
    else if (response.Command == "scopes")
    {
      TryCaptureScopes(response);
    }

    valueConverterService.FormatEvaluateResponse(response);
    return Task.FromResult<ProtocolMessage?>(response);
  }

  private void TryCaptureStackTrace(Response response)
  {
    if (frameSourceTracker is null || response.Body is not JsonElement body || body.ValueKind != JsonValueKind.Object)
    {
      return;
    }

    try
    {
      if (!body.TryGetProperty("stackFrames", out var frames) || frames.ValueKind != JsonValueKind.Array)
      {
        return;
      }

      foreach (var frame in frames.EnumerateArray())
      {
        if (!frame.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var frameId))
        {
          continue;
        }
        if (!frame.TryGetProperty("source", out var srcEl) || srcEl.ValueKind != JsonValueKind.Object)
        {
          continue;
        }
        if (!srcEl.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
        {
          continue;
        }
        var path = pathEl.GetString();
        if (string.IsNullOrEmpty(path))
        {
          continue;
        }
        var line = frame.TryGetProperty("line", out var lineEl) && lineEl.TryGetInt32(out var l) ? l : 1;
        frameSourceTracker.RecordFrame(frameId, path, line);
      }
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "TryCaptureStackTrace failed");
    }
  }

  private void TryCaptureScopes(Response response)
  {
    if (frameSourceTracker is null)
    {
      return;
    }

    try
    {
      var frameId = frameSourceTracker.TakeScopesFrameId(response.RequestSeq);
      if (frameId is null)
      {
        return;
      }

      if (response.Body is not JsonElement body || body.ValueKind != JsonValueKind.Object)
      {
        return;
      }
      if (!body.TryGetProperty("scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
      {
        return;
      }

      foreach (var scope in scopes.EnumerateArray())
      {
        if (!scope.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
        {
          continue;
        }
        var name = nameEl.GetString();
        if (!IsLocalsScopeName(name))
        {
          continue;
        }
        if (!scope.TryGetProperty("variablesReference", out var refEl) || !refEl.TryGetInt32(out var varRef))
        {
          continue;
        }
        frameSourceTracker.RecordLocalsScope(varRef, frameId.Value);
      }
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "TryCaptureScopes failed");
    }
  }

  private static bool IsLocalsScopeName(string? name)
    => name is not null && (name.Equals("Locals", StringComparison.OrdinalIgnoreCase) || name.Equals("Local", StringComparison.OrdinalIgnoreCase));

  private void TryDecorateVariablesWithLocations(VariablesResponse response)
  {
    if (frameSourceTracker is null || variableLocationResolver is null || response.Body?.Variables is null)
    {
      return;
    }

    try
    {
      var varRef = frameSourceTracker.TakeVariablesRef(response.RequestSeq);
      if (varRef is null)
      {
        return;
      }
      var source = frameSourceTracker.TryGetSourceForVarRef(varRef.Value);
      if (source is null)
      {
        return;
      }

      var locations = variableLocationResolver.Resolve(source.Path, source.Line);
      if (locations.Count == 0)
      {
        return;
      }

      foreach (var v in response.Body.Variables)
      {
        if (!locations.TryGetValue(v.Name, out var loc))
        {
          continue;
        }
        v.ExtraProperties ??= [];
        v.ExtraProperties["location"] = JsonSerializer.SerializeToElement(new
        {
          path = loc.Path,
          line = loc.Line,
          column = loc.Column,
        });
      }
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "TryDecorateVariablesWithLocations failed");
    }
  }

  private Task<ProtocolMessage?> HandleEvent(Event evt)
  {

    if (evt.EventName == "stopped")
    {
      valueConverterService.ClearVariablesReferenceMap();
      frameSourceTracker?.Clear();
    }
    else if (evt.EventName == "continued")
    {
      frameSourceTracker?.Clear();
    }
    else if (evt.EventName == "exited")
    {
      LogExitedEvent(evt);
    }
    else if (evt.EventName == "process")
    {
      HandleProcessEvent(evt);
    }
    else if (evt.EventName == "terminated")
    {
      LogTerminatedEvent();
    }

    logger.LogDebug("[DEBUGGER] Event: {event}", evt.EventName);
    return Task.FromResult<ProtocolMessage?>(evt);
  }

  private void HandleProcessEvent(Event evt)
  {
    if (evt.Body.HasValue && evt.Body.Value.ValueKind == JsonValueKind.Object)
    {
      int? processId = null;

      if (evt.Body.Value.TryGetProperty("systemProcessId", out var pidElement))
      {
        processId = pidElement.GetInt32();
      }
      else if (evt.Body.Value.TryGetProperty("processId", out pidElement))
      {
        processId = pidElement.GetInt32();
      }

      if (processId > 0)
      {
        logger.LogInformation("[DEBUGGER] Process event - debugee process started: {processId}", processId.Value);
        onDebugeeProcessStarted(processId.Value);
      }
      else
      {
        logger.LogWarning("[DEBUGGER] Process event received but no valid processId found in body");
      }
    }
    else
    {
      logger.LogWarning("[DEBUGGER] Process event received but body is missing or not an object");
    }
  }

  private void LogTerminatedEvent() => logger.LogInformation("[DEBUGGER] Debug session terminated");

  private void LogExitedEvent(Event evt)
  {
    var exitCode = GetExitCode(evt);
    if (exitCode == 0)
    {
      logger.LogInformation("[DEBUGGER] Program completed successfully (exit code: 0)");
    }
    else
    {
      logger.LogWarning("[DEBUGGER] Program exited with error (exit code: {exitCode})", exitCode);
    }
  }

  private static int? GetExitCode(Event evt) => evt.Body.HasValue && evt.Body.Value.ValueKind == JsonValueKind.Object && evt.Body.Value.TryGetProperty("exitCode", out var exitCodeElement)
      ? exitCodeElement.GetInt32()
      : null;
}