using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services.NetCoreDbg;

public class TcpDapClient(ILogger<TcpDapClient> logger, Action<string> callback) : IDisposable
{
  private TcpListener? _listener;
  private TcpClient? _client;
  private readonly CancellationTokenSource _cts = new();

  private readonly Action<string>? _globalMessageHandler = callback;

  /// <summary>
  /// Starts the TCP server and waits for a client. 
  /// Invokes <paramref name="onClientDisconnected"/> when the client disconnects.
  /// </summary>
  public async Task StartAndConnect(Func<Task>? onClientDisconnected = null)
  {
    _listener = new TcpListener(IPAddress.Any, 8086);
    _listener.Start();
    _client = await _listener.AcceptTcpClientAsync();

    logger.LogInformation("Client connected.");

    StartReadingLoop(_client, _cts.Token, async () =>
    {
      logger.LogInformation("Client disconnected.");
      if (onClientDisconnected != null)
      {
        await onClientDisconnected();
      }
    });
  }

  /// <summary>
  /// Sends a raw message to TCP client
  /// </summary>
  public async Task SendMessageAsync(string json, CancellationToken? cancellationToken = null) => await WriteDapMessageAsync(json, cancellationToken ?? CancellationToken.None);


  private async Task WriteDapMessageAsync(string json, CancellationToken cancellationToken)
  {
    var stream = _client?.GetStream();
    if (stream is null) return;
    await DapMessageWriter.WriteDapMessageAsync(json, stream, cancellationToken);
    logger.LogDebug("[TCP]: Sent message: {Json}", json);
  }

  private void StartReadingLoop(TcpClient client, CancellationToken cancellationToken, Func<Task>? onDisconnect = null) => Task.Run(async () =>
   {
     try
     {
       using (client)
       {
         var stream = client.GetStream();
         while (!cancellationToken.IsCancellationRequested)
         {
           var json = await DapMessageReader.ReadDapMessageAsync(stream, cancellationToken);
           if (json == null) break; // client disconnected
           _globalMessageHandler?.Invoke(json);
         }
       }
     }
     catch (OperationCanceledException) { /* Expected on Dispose */ }
     catch (Exception ex)
     {
       logger.LogError(ex, "Error in TCP reading loop");
     }
     finally
     {
       if (onDisconnect != null)
         await onDisconnect();
     }
   }, cancellationToken);

  public void Dispose()
  {
    _cts.Cancel();

    try
    {
      _client?.Close();
      _client?.Dispose();
    }
    catch { }

    try
    {
      _listener?.Stop();
    }
    catch { }

    _cts.Dispose();
  }

}