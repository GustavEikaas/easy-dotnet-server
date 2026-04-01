using System.IO.Pipes;
using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.AppWrapper;

public class AppWrapperPipeListener(
    AppWrapperManager manager,
    IEditorProcessManagerService editorProcessManagerService,
    ILogger<AppWrapperPipeListener> logger)
{
  public async Task StartAsync(CancellationToken ct)
  {
    var pipeName = AppWrapperManager.GetBackchannelPipeName();
    logger.LogInformation("AppWrapper backchannel listening on pipe: {PipeName}", pipeName);

    while (!ct.IsCancellationRequested)
    {
      var pipe = new NamedPipeServerStream(
          pipeName,
          PipeDirection.InOut,
          NamedPipeServerStream.MaxAllowedServerInstances,
          PipeTransmissionMode.Byte,
          PipeOptions.Asynchronous);

      await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
      logger.LogDebug("AppWrapper connection accepted.");
      _ = HandleConnectionAsync(pipe);
    }
  }

  private Task HandleConnectionAsync(NamedPipeServerStream pipe)
  {
    var formatter = new SystemTextJsonFormatter
    {
      JsonSerializerOptions = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      }
    };

    var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, pipe, formatter));
    var handler = new AppWrapperConnectionHandler(manager, editorProcessManagerService, rpc);
    rpc.AddLocalRpcTarget(handler);
    rpc.StartListening();

    return rpc.Completion.ContinueWith(
        _ => logger.LogDebug("AppWrapper connection closed."),
        TaskScheduler.Default);
  }
}