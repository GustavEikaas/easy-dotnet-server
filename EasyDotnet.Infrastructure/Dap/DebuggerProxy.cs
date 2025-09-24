using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Dap;

public record Client(Stream Input, Stream Output, Action<DAP.ProtocolMessage, Stream> OnMessage);
public record Debugger(Stream Input, Stream Output, Action<DAP.ProtocolMessage, Stream> OnMessage);

public class DebuggerProxy(Client client, Debugger debugger, ILogger<DebuggerProxy>? logger)
{
  private readonly TaskCompletionSource<bool> _completionSource = new();
  private readonly ConcurrentDictionary<int, TaskCompletionSource<DAP.Response>> _requestQueue = new();
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

  public async Task<T> RunInternalDebuggerRequestAsync<T>(string message, int sequence, CancellationToken cancellationToken)
      where T : DAP.Response
  {
    logger?.LogInformation("Running internal request for sequence {seq} {msg}", sequence, message);
    var t = new TaskCompletionSource<DAP.Response>();

    _requestQueue[sequence] = t;

    try
    {
      await DapMessageWriter.WriteDapMessageAsync(message, debugger.Input, cancellationToken);
      logger?.LogInformation("Sent internal request, waiting for response with RequestSeq: {seq}", sequence);

      using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
      using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

      var res = await t.Task.WaitAsync(combinedCts.Token);
      logger?.LogInformation("Received response for internal request: {seq}", sequence);
      return res is T typedResponse
        ? typedResponse
        : throw new InvalidOperationException($"Response could not be cast to {typeof(T).Name}. Actual type: {res.GetType().Name}");
    }
    catch (OperationCanceledException)
    {
      logger?.LogWarning("Internal request {seq} timed out", sequence);
      throw new TimeoutException($"Internal request {sequence} timed out");
    }
    finally
    {
      _requestQueue.TryRemove(sequence, out _);
    }
  }

  private Task StartReadingLoop(Stream outputStream, Stream inputStream, Action<DAP.ProtocolMessage, Stream> onMessage, CancellationToken cancellationToken,
      Action? onDisconnect = null) =>
    Task.Run(async () =>
    {
      try
      {
        while (!cancellationToken.IsCancellationRequested)
        {
          var json = await DapMessageReader.ReadDapMessageAsync(outputStream, cancellationToken) ?? throw new IOException("Stream closed - received null message");
          var msg = DapMessageDeserializer.Parse(json);
          if (msg is DAP.Response response && _requestQueue.TryGetValue(response.RequestSeq, out var taskCompletionSource))
          {
            _requestQueue.TryRemove(response.RequestSeq, out _);
            taskCompletionSource.SetResult(response);
          }
          else
          {
            onMessage(msg, inputStream);
          }
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