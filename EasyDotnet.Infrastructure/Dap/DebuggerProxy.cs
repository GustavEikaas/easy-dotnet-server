using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Dap;

public record Client(Stream Input, Stream Output, Action<ProtocolMessage, Stream> OnMessage);
public record Debugger(Stream Input, Stream Output, Action<ProtocolMessage, Stream> OnMessage);

public class DebuggerProxy(Client client, Debugger debugger, ILogger<DebuggerProxy>? logger)
{
  private readonly TaskCompletionSource<bool> _completionSource = new();
  private readonly ConcurrentDictionary<int, TaskCompletionSource<Response>> _requestQueue = new();
  public Task Completion => _completionSource.Task;

  public void Start(CancellationToken cancellationToken, Action? onDisconnect = null)
  {
    var callbackCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    var clientTask = StartReadingLoop(client.Output, debugger.Input, client.OnMessage,
        callbackCancellationSource.Token, () =>
        {
          logger?.LogInformation("Client stream disconnected.");
          callbackCancellationSource.Cancel();
          onDisconnect?.Invoke();
        });

    var debuggerTask = StartReadingLoop(debugger.Output, client.Input, debugger.OnMessage,
        callbackCancellationSource.Token, () =>
        {
          logger?.LogInformation("Debugger stream disconnected.");
          callbackCancellationSource.Cancel();
          onDisconnect?.Invoke();
        });

    Task.WhenAll(clientTask, debuggerTask).ContinueWith(t =>
    {
      if (t.IsFaulted)
      {
        _completionSource.SetException(t.Exception.InnerException ?? t.Exception);
      }
      else if (t.IsCanceled)
      {
        _completionSource.SetCanceled();
      }
      else
      {
        _completionSource.SetResult(true);
      }
      callbackCancellationSource.Dispose();
    }, cancellationToken);
  }

  public async Task<Response> RunInternalDebuggerRequestAsync(string message, int sequence, CancellationToken cancellationToken)
  {
    logger?.LogInformation("Running internal request for sequence {seq} {msg}", sequence, message);
    var t = new TaskCompletionSource<Response>();

    // Add to queue BEFORE sending the request
    _requestQueue[sequence] = t;

    try
    {
      await DapMessageWriter.WriteDapMessageAsync(message, debugger.Input, cancellationToken);
      logger?.LogInformation("Sent internal request, waiting for response with RequestSeq: {seq}", sequence);

      // Add timeout to prevent hanging
      using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
      using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

      var res = await t.Task.WaitAsync(combinedCts.Token);
      logger?.LogInformation("Received response for internal request: {seq}", sequence);
      return res;
    }
    catch (OperationCanceledException)
    {
      logger?.LogWarning("Internal request {seq} timed out", sequence);
      throw new TimeoutException($"Internal request {sequence} timed out");
    }
    finally
    {
      _requestQueue.TryRemove(sequence, out _); // Clean up
    }
  }

  private Task StartReadingLoop(Stream outputStream, Stream inputStream, Action<ProtocolMessage, Stream> onMessage, CancellationToken cancellationToken,
      Action? onDisconnect = null) =>
    Task.Run(async () =>
    {
      try
      {
        while (!cancellationToken.IsCancellationRequested)
        {
          var json = await DapMessageReader.ReadDapMessageAsync(outputStream, cancellationToken) ?? throw new IOException("Stream closed - received null message");
          var msg = DapMessageDeserializer.Parse(json);
          if (msg is Response response && _requestQueue.TryGetValue(response.RequestSeq, out var taskCompletionSource))
          {
            logger?.LogInformation("Responding to internal request for sequence {seq}", response.RequestSeq);
            _requestQueue.TryRemove(response.RequestSeq, out _);
            taskCompletionSource.SetResult(response);
          }
          else
          {
            onMessage(msg, inputStream);
          }

          // // Check if this is a response to an internal request FIRST
          // if (msg is Response response && _requestQueue.TryGetValue(response.RequestSeq, out var taskCompletionSource))
          // {
          //   logger?.LogInformation("Responding to internal request for sequence {seq}", response.RequestSeq);
          //   _requestQueue.TryRemove(response.RequestSeq, out _);
          //   taskCompletionSource.SetResult(response);
          //   // Don't forward this response to the client - it was our internal request
          //   continue;
          // }
          //
          // This is a normal message, process with refiner and forward
          // await DapMessageWriter.WriteDapMessageAsync(message, inputStream, cancellationToken);
        }
      }
      catch (OperationCanceledException) { }
      catch (IOException ex)
      {
        throw new Exception($"Stream disconnected: {ex.Message}", ex);
      }
      catch (Exception ex)
      {
        throw new Exception("Reading loop failed.", ex);
      }
      finally
      {
        onDisconnect?.Invoke();
      }
    }, cancellationToken);
}