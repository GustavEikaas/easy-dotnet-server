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
  Action<DebugOutputEvent>? handleOutput = null) : IDapMessageInterceptor
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

    if (evt.EventName == "output" && handleOutput != null)
    {
      HandleOutputEvent(evt);
      return Task.FromResult<ProtocolMessage?>(null);
    }


    logger.LogDebug("[DEBUGGER] Event: {event}", evt.EventName);
    return Task.FromResult<ProtocolMessage?>(evt);
  }

  private void HandleOutputEvent(Event evt)
  {
    try
    {
      if (evt.Body is not JsonElement bodyJson)
      {
        logger.LogWarning("Output event body is not a JsonElement");
        return;
      }

      if (!bodyJson.TryGetProperty("output", out var outputElement))
      {
        logger.LogWarning("Output event missing 'output' property");
        return;
      }

      var output = outputElement.GetString();
      if (string.IsNullOrEmpty(output))
      {
        return;
      }

      // Extract category (default to "console")
      var category = bodyJson.TryGetProperty("category", out var categoryElement)
        ? categoryElement.GetString() ?? "console"
        : "console";

      // Extract optional source information
      DebugOutputSource? source = null;
      if (bodyJson.TryGetProperty("source", out var sourceElement))
      {
        source = new DebugOutputSource
        {
          Name = sourceElement.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()
            : null,
          Path = sourceElement.TryGetProperty("path", out var pathEl)
            ? pathEl.GetString()
            : null,
          SourceReference = sourceElement.TryGetProperty("sourceReference", out var refEl)
            ? refEl.GetInt32()
            : null
        };
      }

      // Extract optional line number
      int? line = bodyJson.TryGetProperty("line", out var lineElement)
        ? lineElement.GetInt32()
        : null;

      // Extract optional column
      int? column = bodyJson.TryGetProperty("column", out var columnElement)
        ? columnElement.GetInt32()
        : null;

      // Extract optional group
      var group = bodyJson.TryGetProperty("group", out var groupElement)
        ? groupElement.GetString()
        : null;

      // Extract optional data (can be any JSON)
      object? data = null;
      if (bodyJson.TryGetProperty("data", out var dataElement))
      {
        data = dataElement.ValueKind switch
        {
          JsonValueKind.String => dataElement.GetString(),
          JsonValueKind.Number => dataElement.GetDouble(),
          JsonValueKind.True => true,
          JsonValueKind.False => false,
          JsonValueKind.Object => dataElement.Clone(),
          JsonValueKind.Array => dataElement.Clone(),
          _ => null
        };
      }

      // Create the output event
      var outputEvent = new DebugOutputEvent
      {
        Output = output,
        Category = category,
        Source = source,
        Line = line,
        Column = column,
        Group = group,
        Data = data,
        Timestamp = DateTime.UtcNow
      };

      // Forward to external handler
      handleOutput?.Invoke(outputEvent);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to process output event");
    }
  }
}