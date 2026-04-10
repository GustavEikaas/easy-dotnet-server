using System.IO.Pipes;
using System.Text.Json;
using EasyDotnet.IDE.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.IDE.AppWrapper;

public class AppWrapperPipeListener(
    AppWrapperManager manager,
    CurrentLogLevel currentLogLevel,
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
    var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, pipe, CreateJsonMessageFormatter()));
    var handler = new AppWrapperConnectionHandler(manager, editorProcessManagerService, rpc);
    rpc.TraceSource.Switch.Level = currentLogLevel.Loglevel;
    rpc.TraceSource.Listeners.Clear();
    rpc.TraceSource.Listeners.Add(new JsonRpcLogger(logger, "AppWrapper"));
    rpc.AddLocalRpcTarget(handler);
    rpc.StartListening();

    return rpc.Completion.ContinueWith(
        _ => logger.LogDebug("AppWrapper connection closed."),
        TaskScheduler.Default);

  }

  private static JsonMessageFormatter CreateJsonMessageFormatter() => new()
  {
    JsonSerializer = { ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }}
  };
}