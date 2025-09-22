using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Dap;

public record Client(Stream Input, Stream Output, Func<ProtocolMessage, Task<string>>? MessageRefiner);
public record Debugger(Stream Input, Stream Output, Func<ProtocolMessage, Task<string>>? MessageRefiner);

public class DebuggerProxy(Client client, Debugger debugger, ILogger<DebuggerProxy>? logger)
{
  private readonly TaskCompletionSource<bool> _completionSource = new();
  private readonly Dictionary<int, TaskCompletionSource<Response>> _requestQueue = [];
  public Task Completion => _completionSource.Task;

  public void Start(CancellationToken cancellationToken, Action? onDisconnect = null)
  {
    var callbackCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    var clientTask = StartReadingLoop(client.Output, debugger.Input, client.MessageRefiner,
        callbackCancellationSource.Token, () =>
        {
          logger?.LogInformation("Client stream disconnected.");
          callbackCancellationSource.Cancel();
          onDisconnect?.Invoke();
        });

    var debuggerTask = StartReadingLoop(debugger.Output, client.Input, debugger.MessageRefiner,
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
    logger?.LogInformation("Running internal request for sequence {seq}", sequence);
    var t = new TaskCompletionSource<Response>();
    _requestQueue.Add(sequence, t);

    await DapMessageWriter.WriteDapMessageAsync(message, debugger.Input, cancellationToken);
    var res = await t.Task;
    return res;
  }

  private Task StartReadingLoop(Stream outputStream, Stream inputStream,
      Func<ProtocolMessage, Task<string>>? messageRefiner, CancellationToken cancellationToken,
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
            logger?.LogInformation("Responding internal request for sequence {seq}", response.RequestSeq);
            taskCompletionSource.SetResult(response);
          }
          else
          {
            var message = messageRefiner != null ? await messageRefiner(msg) : json;
            await DapMessageWriter.WriteDapMessageAsync(message, inputStream, cancellationToken);
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