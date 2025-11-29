using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Dap;

public record Client(Stream Input, Stream Output, Func<ProtocolMessage, IDebuggerProxy, Task<string>>? MessageRefiner);
public record Debugger(Stream Input, Stream Output, Func<ProtocolMessage, IDebuggerProxy, Task<string>>? MessageRefiner);

public interface IDebuggerProxy
{
  Task Completion { get; }
  void Start(CancellationToken cancellationToken, Action? onDisconnect = null);
  Task<Response> RunInternalRequestAsync(Request request, CancellationToken cancellationToken);
}

public class DebuggerProxy : IDebuggerProxy
{
  private readonly Client _client;
  private readonly Debugger _debugger;
  private readonly IMessageChannels _channels;
  private readonly IRequestTracker _requestTracker;
  private readonly IMessageProcessor _messageProcessor;
  private readonly ILogger<DebuggerProxy>? _logger;
  private readonly TaskCompletionSource<bool> _completionSource = new();

  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  public Task Completion => _completionSource.Task;

  public DebuggerProxy(
    Client client,
    Debugger debugger,
    ILogger<DebuggerProxy>? logger = null)
    : this(
      client,
      debugger,
      new MessageChannels(),
      new RequestTracker(),
      logger)
  {
  }

  // Constructor for testing with injected dependencies
  public DebuggerProxy(
    Client client,
    Debugger debugger,
    IMessageChannels channels,
    IRequestTracker requestTracker,
    ILogger<DebuggerProxy>? logger = null)
  {
    _client = client;
    _debugger = debugger;
    _channels = channels;
    _requestTracker = requestTracker;
    _logger = logger;

    _messageProcessor = new MessageProcessor(
      _channels,
      _requestTracker,
      this, // Pass proxy instance to processor
      _client.MessageRefiner,
      _debugger.MessageRefiner,
      logger != null ? new LoggerFactory().CreateLogger<MessageProcessor>() : null
    );
  }

  public void Start(CancellationToken cancellationToken, Action? onDisconnect = null)
  {
    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    var clientReaderTask = StartClientReaderAsync(linkedCts.Token, () =>
    {
      _logger?.LogInformation("Client stream disconnected");
      linkedCts.Cancel();
      onDisconnect?.Invoke();
    });

    var debuggerReaderTask = StartDebuggerReaderAsync(linkedCts.Token, () =>
    {
      _logger?.LogInformation("Debugger stream disconnected");
      linkedCts.Cancel();
      onDisconnect?.Invoke();
    });

    var clientWriterTask = StartClientWriterAsync(linkedCts.Token);
    var debuggerWriterTask = StartDebuggerWriterAsync(linkedCts.Token);
    var processorTask = _messageProcessor.ProcessMessagesAsync(linkedCts.Token);

    Task.WhenAll(
      clientReaderTask,
      debuggerReaderTask,
      clientWriterTask,
      debuggerWriterTask,
      processorTask
    ).ContinueWith(t =>
    {
      _channels.CompleteAll();
      _requestTracker.Clear();

      if (t.IsFaulted)
      {
        _completionSource.SetException(t.Exception?.InnerException ?? t.Exception!);
      }
      else if (t.IsCanceled)
      {
        _completionSource.SetCanceled();
      }
      else
      {
        _completionSource.SetResult(true);
      }

      linkedCts.Dispose();
    }, cancellationToken);
  }

  public async Task<Response> RunInternalRequestAsync(Request request, CancellationToken cancellationToken)
  {
    var tcs = new TaskCompletionSource<Response>();

    // Register the internal request and get proxy seq
    var proxySeq = _requestTracker.RegisterProxyRequest(tcs, cancellationToken);
    request.Seq = proxySeq;

    _logger?.LogDebug("Proxy internal request seq {proxySeq}, command: {command}", proxySeq, request.Command);

    // Serialize and send to debugger
    var json = JsonSerializer.Serialize(request, SerializerOptions);
    await _channels.ProxyToDebuggerWriter.WriteAsync(json, cancellationToken);

    // Wait for response with cancellation support
    using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
    return await tcs.Task;
  }

  private async Task StartClientReaderAsync(CancellationToken cancellationToken, Action? onDisconnect)
  {
    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        var json = await DapMessageReader.ReadDapMessageAsync(_client.Output, cancellationToken);
        if (json == null)
        {
          _logger?.LogInformation("Client stream closed");
          break;
        }

        var message = DapMessageDeserializer.Parse(json);
        await _channels.ClientToProxyWriter.WriteAsync(message, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      _logger?.LogDebug("Client reader cancelled");
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Client reader failed");
      throw;
    }
    finally
    {
      _channels.ClientToProxyWriter.Complete();
      onDisconnect?.Invoke();
    }
  }

  private async Task StartDebuggerReaderAsync(CancellationToken cancellationToken, Action? onDisconnect)
  {
    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        var json = await DapMessageReader.ReadDapMessageAsync(_debugger.Output, cancellationToken);
        if (json == null)
        {
          _logger?.LogInformation("Debugger stream closed");
          break;
        }

        var message = DapMessageDeserializer.Parse(json);
        await _channels.DebuggerToProxyWriter.WriteAsync(message, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      _logger?.LogDebug("Debugger reader cancelled");
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Debugger reader failed");
      throw;
    }
    finally
    {
      _channels.DebuggerToProxyWriter.Complete();
      onDisconnect?.Invoke();
    }
  }

  private async Task StartClientWriterAsync(CancellationToken cancellationToken)
  {
    try
    {
      await foreach (var json in _channels.ProxyToClientReader.ReadAllAsync(cancellationToken))
      {
        await DapMessageWriter.WriteDapMessageAsync(json, _client.Input, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      _logger?.LogDebug("Client writer cancelled");
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Client writer failed");
      throw;
    }
  }

  private async Task StartDebuggerWriterAsync(CancellationToken cancellationToken)
  {
    try
    {
      await foreach (var json in _channels.ProxyToDebuggerReader.ReadAllAsync(cancellationToken))
      {
        await DapMessageWriter.WriteDapMessageAsync(json, _debugger.Input, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      _logger?.LogDebug("Debugger writer cancelled");
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Debugger writer failed");
      throw;
    }
  }
}