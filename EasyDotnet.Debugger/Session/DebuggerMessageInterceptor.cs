using System.Text.Json;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class DebuggerMessageInterceptor(
  ILogger<DebuggerMessageInterceptor> logger,
  ValueConverterService valueConverterService,
  bool applyValueConverters) : IDapMessageInterceptor
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

    if (applyValueConverters)
    {
      valueConverterService.RegisterVariablesReferences(response);
    }

    return Task.FromResult<ProtocolMessage?>(response);
  }

  private Task<ProtocolMessage?> HandleResponse(Response response)
  {
    logger.LogDebug("[DEBUGGER] Response: {command}", response.Command);
    return Task.FromResult<ProtocolMessage?>(response);
  }

  private Task<ProtocolMessage?> HandleEvent(Event evt)
  {

    if (evt.EventName == "stopped")
    {
      valueConverterService.ClearVariablesReferenceMap();
    }

    if (evt.EventName == "exited")
    {
      LogExitedEvent(evt);
    }
    else if (evt.EventName == "terminated")
    {
      LogTerminatedEvent();
    }

    logger.LogDebug("[DEBUGGER] Event: {event}", evt.EventName);
    return Task.FromResult<ProtocolMessage?>(evt);
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