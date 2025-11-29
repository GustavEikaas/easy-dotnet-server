using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Dap;

public interface IMessageProcessor
{
  Task ProcessMessagesAsync(CancellationToken cancellationToken);
}

public class MessageProcessor : IMessageProcessor
{
  private readonly IMessageChannels _channels;
  private readonly IRequestTracker _requestTracker;
  private readonly IDebuggerProxy _proxy;
  private readonly Func<ProtocolMessage, IDebuggerProxy, Task<string>>? _clientMessageRefiner;
  private readonly Func<ProtocolMessage, IDebuggerProxy, Task<string>>? _debuggerMessageRefiner;
  private readonly ILogger<MessageProcessor>? _logger;

  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  public MessageProcessor(
    IMessageChannels channels,
    IRequestTracker requestTracker,
    IDebuggerProxy proxy,
    Func<ProtocolMessage, IDebuggerProxy, Task<string>>? clientMessageRefiner = null,
    Func<ProtocolMessage, IDebuggerProxy, Task<string>>? debuggerMessageRefiner = null,
    ILogger<MessageProcessor>? logger = null)
  {
    _channels = channels;
    _requestTracker = requestTracker;
    _proxy = proxy;
    _clientMessageRefiner = clientMessageRefiner;
    _debuggerMessageRefiner = debuggerMessageRefiner;
    _logger = logger;
  }

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
      await foreach (var message in _channels.ClientToProxyReader.ReadAllAsync(cancellationToken))
      {
        await HandleClientMessageAsync(message, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      _logger?.LogInformation("Client message processing cancelled");
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Error processing client messages");
      throw;
    }
  }

  private async Task ProcessDebuggerMessagesAsync(CancellationToken cancellationToken)
  {
    var tasks = new List<Task>();

    try
    {
      await foreach (var message in _channels.DebuggerToProxyReader.ReadAllAsync(cancellationToken))
      {
        // Process messages concurrently to avoid blocking when refiners make internal requests
        var task = Task.Run(async () =>
        {
          try
          {
            await HandleDebuggerMessageAsync(message, cancellationToken);
          }
          catch (Exception ex)
          {
            _logger?.LogError(ex, "Error handling debugger message");
            throw; // Re-throw to propagate to Task.WhenAll
          }
        }, cancellationToken);

        tasks.Add(task);
      }

      // Wait for all message handlers to complete
      await Task.WhenAll(tasks);
    }
    catch (OperationCanceledException)
    {
      _logger?.LogInformation("Debugger message processing cancelled");
      // Wait for any in-flight handlers
      await Task.WhenAll(tasks.Where(t => !t.IsCompleted));
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Error processing debugger messages");
      throw;
    }
  }

  private async Task HandleClientMessageAsync(ProtocolMessage message, CancellationToken cancellationToken)
  {
    if (message is not Request request)
    {
      _logger?.LogWarning("Unexpected non-request message from client: {type}", message.Type);
      return;
    }

    // Register and assign new proxy seq
    var originalSeq = request.Seq;
    var proxySeq = _requestTracker.RegisterClientRequest(originalSeq);
    request.Seq = proxySeq;

    _logger?.LogDebug("Client request {originalSeq} -> proxy seq {proxySeq}, command: {command}",
      originalSeq, proxySeq, request.Command);

    // Apply message refiner if available
    var json = _clientMessageRefiner != null
      ? await _clientMessageRefiner(request, _proxy)
      : JsonSerializer.Serialize(request, SerializerOptions);

    // Send to debugger
    await _channels.ProxyToDebuggerWriter.WriteAsync(json, cancellationToken);
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
        _logger?.LogWarning("Unexpected message type from debugger: {type}", message.Type);
        break;
    }
  }

  private async Task HandleDebuggerResponseAsync(Response response, CancellationToken cancellationToken)
  {
    _logger?.LogDebug("HandleDebuggerResponseAsync START: seq={seq}, command={command}", response.RequestSeq, response.Command);

    var context = _requestTracker.GetAndRemoveContext(response.RequestSeq);

    if (context == null)
    {
      _logger?.LogWarning("Received response for unknown request seq: {seq}", response.RequestSeq);
      return;
    }

    _logger?.LogDebug("Debugger response for seq {proxySeq}, origin: {origin}, command: {command}",
      response.RequestSeq, context.Origin, response.Command);

    if (context.Origin == RequestOrigin.Proxy)
    {
      _logger?.LogDebug("Completing proxy request: seq={seq}", response.RequestSeq);
      // Complete the internal request WITHOUT running refiners
      // This prevents deadlocks when refiners make internal requests
      // Refiners only run on client-originated traffic
      context.CompletionSource.TrySetResult(response);
      _logger?.LogDebug("Completed proxy request: seq={seq}", response.RequestSeq);
      return;
    }

    _logger?.LogDebug("Processing client response: seq={seq}, about to run refiner", response.RequestSeq);

    // Client-originated request - restore original client seq
    response.Seq = response.Seq; // Keep response seq as-is
    response.RequestSeq = context.OriginalSeq;

    // Apply message refiner if available
    // This can safely make internal requests since we're not blocking the processor
    var json = _debuggerMessageRefiner != null
      ? await _debuggerMessageRefiner(response, _proxy)
      : JsonSerializer.Serialize(response, SerializerOptions);

    _logger?.LogDebug("Refiner completed for seq={seq}, writing to client", context.OriginalSeq);

    // Send to client
    await _channels.ProxyToClientWriter.WriteAsync(json, cancellationToken);

    _logger?.LogDebug("HandleDebuggerResponseAsync END: seq={seq}", context.OriginalSeq);
  }

  private async Task HandleDebuggerEventAsync(Event evt, CancellationToken cancellationToken)
  {
    _logger?.LogDebug("Debugger event: {eventName}", evt.EventName);

    // Apply message refiner if available
    var json = _debuggerMessageRefiner != null
      ? await _debuggerMessageRefiner(evt, _proxy)
      : JsonSerializer.Serialize(evt, SerializerOptions);

    // Forward all events to client
    await _channels.ProxyToClientWriter.WriteAsync(json, cancellationToken);
  }
}