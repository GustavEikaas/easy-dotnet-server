using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Messages;

public interface IMessageProcessor
{
  Task ProcessMessagesAsync(CancellationToken cancellationToken);
}

public class MessageProcessor(
  IMessageChannels channels,
  IRequestTracker requestTracker,
  IDebuggerProxy proxy,
  Func<ProtocolMessage, IDebuggerProxy, Task<string?>>? clientMessageRefiner = null,
  Func<ProtocolMessage, IDebuggerProxy, Task<string?>>? debuggerMessageRefiner = null,
  ILogger<MessageProcessor>? logger = null) : IMessageProcessor
{
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  public async Task ProcessMessagesAsync(CancellationToken cancellationToken)
  {
    var clientTask = ProcessClientMessagesAsync(cancellationToken);
    var debuggerTask = ProcessDebuggerMessagesAsync(cancellationToken);

    await Task.WhenAll(clientTask, debuggerTask);
  }

  private async Task ProcessClientMessagesAsync(CancellationToken cancellationToken)
  {
    try
    {
      await foreach (var message in channels.ClientToProxyReader.ReadAllAsync(cancellationToken))
      {
        await HandleClientMessageAsync(message, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      logger?.LogInformation("Client message processing cancelled");
    }
    catch (Exception ex)
    {
      logger?.LogError(ex, "Error processing client messages");
      throw;
    }
  }

  private async Task ProcessDebuggerMessagesAsync(CancellationToken cancellationToken)
  {
    var tasks = new List<Task>();

    try
    {
      await foreach (var message in channels.DebuggerToProxyReader.ReadAllAsync(cancellationToken))
      {
        var task = Task.Run(async () =>
        {
          try
          {
            await HandleDebuggerMessageAsync(message, cancellationToken);
          }
          catch (Exception ex)
          {
            logger?.LogError(ex, "Error handling debugger message");
            throw;
          }
        }, cancellationToken);

        tasks.Add(task);
      }

      await Task.WhenAll(tasks);
    }
    catch (OperationCanceledException)
    {
      logger?.LogInformation("Debugger message processing cancelled");
      await Task.WhenAll(tasks.Where(t => !t.IsCompleted));
    }
    catch (Exception ex)
    {
      logger?.LogError(ex, "Error processing debugger messages");
      throw;
    }
  }

  private async Task HandleClientMessageAsync(ProtocolMessage message, CancellationToken cancellationToken)
  {
    if (message is not Request request)
    {
      logger?.LogWarning("Unexpected non-request message from client: {type}", message.Type);
      return;
    }

    var originalSeq = request.Seq;
    var proxySeq = requestTracker.RegisterClientRequest(originalSeq);
    request.Seq = proxySeq;

    logger?.LogDebug("Client request {originalSeq} -> proxy seq {proxySeq}, command: {command}",
      originalSeq, proxySeq, request.Command);

    var json = clientMessageRefiner != null
      ? await clientMessageRefiner(request, proxy)
      : JsonSerializer.Serialize(request, SerializerOptions);

    if (json is null)
    {
      return;
    }

    await channels.ProxyToDebuggerWriter.WriteAsync(json, cancellationToken);
  }

  private async Task HandleDebuggerMessageAsync(ProtocolMessage message, CancellationToken cancellationToken)
  {
    switch (message)
    {
      case Response response:
        await HandleDebuggerResponseAsync(response, cancellationToken);
        break;

      case Event evt:
        await HandleDebuggerEventAsync(evt, cancellationToken);
        break;

      default:
        logger?.LogWarning("Unexpected message type from debugger: {type}", message.Type);
        break;
    }
  }

  private async Task HandleDebuggerResponseAsync(Response response, CancellationToken cancellationToken)
  {
    logger?.LogDebug("HandleDebuggerResponseAsync START: seq={seq}, command={command}", response.RequestSeq, response.Command);

    var context = requestTracker.GetAndRemoveContext(response.RequestSeq);

    if (context == null)
    {
      logger?.LogWarning("Received response for unknown request seq: {seq}", response.RequestSeq);
      return;
    }

    logger?.LogDebug("Debugger response for seq {proxySeq}, origin: {origin}, command: {command}",
      response.RequestSeq, context.Origin, response.Command);

    if (context.Origin == RequestOrigin.Proxy)
    {
      logger?.LogDebug("Completing proxy request: seq={seq}", response.RequestSeq);
      context.CompletionSource.TrySetResult(response);
      logger?.LogDebug("Completed proxy request: seq={seq}", response.RequestSeq);
      return;
    }

    logger?.LogDebug("Processing client response: seq={seq}, about to run refiner", response.RequestSeq);

    response.Seq = response.Seq;
    response.RequestSeq = context.OriginalSeq;

    var json = debuggerMessageRefiner != null
      ? await debuggerMessageRefiner(response, proxy)
      : JsonSerializer.Serialize(response, SerializerOptions);

    logger?.LogDebug("Refiner completed for seq={seq}, writing to client", context.OriginalSeq);

    if (json is null)
    {
      return;
    }

    await channels.ProxyToClientWriter.WriteAsync(json, cancellationToken);

    logger?.LogDebug("HandleDebuggerResponseAsync END: seq={seq}", context.OriginalSeq);
  }

  private async Task HandleDebuggerEventAsync(Event evt, CancellationToken cancellationToken)
  {
    logger?.LogDebug("Debugger event: {eventName}", evt.EventName);

    var json = debuggerMessageRefiner != null
      ? await debuggerMessageRefiner(evt, proxy)
      : JsonSerializer.Serialize(evt, SerializerOptions);

    if (json is null)
    {
      return;
    }
    await channels.ProxyToClientWriter.WriteAsync(json, cancellationToken);
  }
}