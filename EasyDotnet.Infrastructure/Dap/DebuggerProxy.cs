using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Dap;

public record Client(Stream Input, Stream Output, Func<string, Task<string>>? MessageRefiner);
public record Debugger(Stream Input, Stream Output, Func<string, Task<string>>? MessageRefiner);

public class DebuggerProxy(Client client, Debugger debugger, ILogger<DebuggerProxy>? logger)
{
  private readonly TaskCompletionSource<bool> _completionSource = new();
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

  private static Task StartReadingLoop(Stream outputStream, Stream inputStream,
      Func<string, Task<string>>? messageRefiner, CancellationToken cancellationToken,
      Action? onDisconnect = null) =>
    Task.Run(async () =>
    {
      try
      {
        while (!cancellationToken.IsCancellationRequested)
        {
          var json = await DapMessageReader.ReadDapMessageAsync(outputStream, cancellationToken) ?? throw new IOException("Stream closed - received null message");

          var message = messageRefiner != null ? await messageRefiner(json) : json;
          await DapMessageWriter.WriteDapMessageAsync(message, inputStream, cancellationToken);
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