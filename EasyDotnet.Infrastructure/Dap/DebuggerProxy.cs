namespace EasyDotnet.Infrastructure.Dap;

public record Client(Stream Input, Stream Output, Func<string, Task<string>>? MessageRefiner);
public record Debugger(Stream Input, Stream Output, Func<string, Task<string>>? MessageRefiner);

public class DebuggerProxy(Client client, Debugger debugger)
{
  private readonly TaskCompletionSource<bool> _completionSource = new();

  public Task Completion => _completionSource.Task;

  public void Start(CancellationToken cancellationToken)
  {
    var callbackCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    var clientTask = StartReadingLoop(client.Output, debugger.Input, client.MessageRefiner, callbackCancellationSource.Token, callbackCancellationSource.Cancel);
    var debuggerTask = StartReadingLoop(debugger.Output, client.Input, debugger.MessageRefiner, callbackCancellationSource.Token, callbackCancellationSource.Cancel);

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

  private static Task StartReadingLoop(Stream outputStream, Stream inputStream, Func<string, Task<string>>? messageRefiner, CancellationToken cancellationToken, Action? onDisconnect = null) =>
    Task.Run(async () =>
     {
       try
       {
         while (!cancellationToken.IsCancellationRequested)
         {
           var json = (await DapMessageReader.ReadDapMessageAsync(outputStream, cancellationToken) ?? throw new IOException("Json from reader was null, Stream closed")) ?? throw new IOException("Client disconnected, stream closed.");
           var message = messageRefiner != null ? await messageRefiner(json) : json;
           await DapMessageWriter.WriteDapMessageAsync(message, inputStream, cancellationToken);
         }
       }
       catch (OperationCanceledException) { /* Expected on Dispose */ }
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