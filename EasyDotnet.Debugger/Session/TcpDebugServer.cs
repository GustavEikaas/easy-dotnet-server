using System.Net;
using System.Net.Sockets;
using EasyDotnet.Debugger.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class TcpDebugServer : ITcpDebugServer
{
  private readonly ILogger<TcpDebugServer> _logger;
  private readonly TcpListener _listener;
  private TcpClient? _client;

  public int Port { get; }

  public TcpDebugServer(ILogger<TcpDebugServer> logger)
  {
    _logger = logger;
    _listener = new TcpListener(IPAddress.Any, 0);
    _listener.Start();
    Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    _logger.LogDebug("TCP server listening on port {port}", Port);
  }

  public async Task<Stream> AcceptClientAsync(TimeSpan timeout, CancellationToken cancellationToken)
  {
    _logger.LogInformation("Waiting for client connection...");

    _client = await _listener.AcceptTcpClientAsync().WaitAsync(timeout, cancellationToken);

    _logger.LogInformation("Client connected from {endpoint}", _client.Client.RemoteEndPoint);

    return _client.GetStream();
  }

  public void Stop()
  {
    try
    {
      _client?.Close();
      _logger.LogDebug("TCP client closed");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error closing TCP client");
    }

    try
    {
      _listener.Stop();
      _logger.LogDebug("TCP listener stopped");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error stopping TCP listener");
    }
  }

  public async ValueTask DisposeAsync()
  {
    Stop();
    await Task.CompletedTask;
  }
}