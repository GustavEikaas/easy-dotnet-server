using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDotnet.Aspire.Models;
using EasyDotnet.Aspire.Server.Handlers;
using EasyDotnet.Aspire.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyDotnet.Aspire.Server;

public sealed class DcpServer : IDcpServer, IAsyncDisposable
{
  private readonly IServiceProvider _serviceProvider;
  private readonly ILogger<DcpServer> _logger;
  private readonly DcpServerOptions _options;
  private readonly HttpListener _listener;
  private readonly Dictionary<string, WebSocket> _webSocketsByDcpId = [];
  private readonly Dictionary<string, Queue<object>> _pendingNotificationsByDcpId = [];
  private readonly CancellationTokenSource _cts = new();
  private readonly SemaphoreSlim _startLock = new(1, 1);
  private Task? _listenerTask;
  private bool _isStarted;

  public int Port { get; private set; }
  public string Token { get; private set; }
  public bool IsRunning => _isStarted && !_cts.Token.IsCancellationRequested;

  public DcpServer(
      IServiceProvider serviceProvider,
      ILogger<DcpServer> logger,
      IOptions<DcpServerOptions> options)
  {
    _serviceProvider = serviceProvider;
    _logger = logger;
    _options = options.Value;
    Token = _options.Token ?? Guid.NewGuid().ToString("N");
    _listener = new HttpListener();
  }

  public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
  {
    if (_isStarted) return;

    await _startLock.WaitAsync(cancellationToken);
    try
    {
      if (_isStarted) return;

      Port = _options.Port == 0 ? GetFreePort() : _options.Port;
      _listener.Prefixes.Add($"http://localhost:{Port}/");
      _listener.Start();

      StartListening();
      _isStarted = true;

      _logger.LogInformation("DCP server started on port {Port}", Port);
    }
    finally
    {
      _startLock.Release();
    }
  }

  private void StartListening() => _listenerTask = Task.Run(async () =>
  {
    while (!_cts.Token.IsCancellationRequested)
    {
      try
      {
        var context = await _listener.GetContextAsync();
        _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
      }
      catch (HttpListenerException) when (_cts.Token.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error accepting HTTP request");
      }
    }
  });

  private async Task HandleRequestAsync(HttpListenerContext context)
  {
    try
    {
      var request = context.Request;
      var response = context.Response;
      var path = request.Url!.AbsolutePath;
      var method = request.HttpMethod;

      // Auth check (not required for /info endpoint per spec)
      if (path != "/info")
      {
        var authHeader = request.Headers["Authorization"];
        var dcpId = request.Headers["Microsoft-Developer-DCP-Instance-ID"];

        if (string.IsNullOrEmpty(authHeader) || string.IsNullOrEmpty(dcpId))
        {
          await SendErrorResponseAsync(response, 401, "MissingHeaders",
              "Authorization and Microsoft-Developer-DCP-Instance-ID headers are required.");
          return;
        }

        if (!authHeader.StartsWith("Bearer "))
        {
          await SendErrorResponseAsync(response, 401, "InvalidAuthHeader",
              "Authorization header must start with 'Bearer '");
          return;
        }

        var providedToken = authHeader.Substring("Bearer ".Length);

        // Look up session by token instead of validating against shared token
        using var scope = _serviceProvider.CreateScope();
        var sessionManager = scope.ServiceProvider.GetRequiredService<IAspireSessionManager>();
        var session = sessionManager.GetSessionByToken(providedToken);

        if (session == null)
        {
          await SendErrorResponseAsync(response, 401, "InvalidToken",
              "Invalid or unknown token in Authorization header.");
          return;
        }

        // Set DCP ID if this is the first request from this DCP instance
        if (string.IsNullOrEmpty(session.DcpId))
        {
          sessionManager.SetSessionDcpId(providedToken, dcpId);
          _logger.LogInformation(
              "Associated DCP ID {DcpId} with session token {Token}",
              dcpId,
              providedToken);
        }
      }

      // Route the request
      if (path == "/info" && method == "GET")
      {
        await HandleInfoAsync(response);
      }
      else if (path.StartsWith("/run_session") && method == "PUT")
      {
        await HandleCreateRunSessionAsync(request, response);
      }
      else if (path.StartsWith("/run_session/") && method == "DELETE")
      {
        var runId = path.Substring("/run_session/".Length);
        await HandleDeleteRunSessionAsync(runId, response);
      }
      else if (path == "/run_session/notify" && request.IsWebSocketRequest)
      {
        await HandleWebSocketAsync(context);
      }
      else
      {
        response.StatusCode = 404;
        response.Close();
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling request");
      try
      {
        context.Response.StatusCode = 500;
        context.Response.Close();
      }
      catch { }
    }
  }

  private async Task HandleInfoAsync(HttpListenerResponse response)
  {
    var runSessionInfo = new
    {
      ProtocolsSupported = _options.SupportedProtocols,
      SupportedLaunchConfigurations = _options.SupportedLaunchConfigurations
    };
    await SendJsonResponseAsync(response, 200, runSessionInfo);
  }

  private async Task HandleCreateRunSessionAsync(HttpListenerRequest request, HttpListenerResponse response)
  {
    var dcpId = request.Headers["Microsoft-Developer-DCP-Instance-ID"]!;

    using var reader = new StreamReader(request.InputStream);
    var json = await reader.ReadToEndAsync();

    var options = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
      Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };


    var payload = JsonSerializer.Deserialize<RunSessionPayload>(json, options);

    _logger.LogInformation("Received run session request from DCP ID {DcpId}", dcpId);

    if (payload == null || payload.LaunchConfigurations.Length == 0)
    {
      await SendErrorResponseAsync(response, 400, "InvalidPayload", "Invalid request payload");
      return;
    }

    var launchConfig = payload.LaunchConfigurations[0];

    if (launchConfig.Type != "project")
    {
      await SendErrorResponseAsync(response, 400, "UnsupportedLaunchConfiguration",
          "Unsupported launch configuration type");
      return;
    }

    if (string.IsNullOrEmpty(launchConfig.ProjectPath))
    {
      await SendErrorResponseAsync(response, 400, "InvalidProjectConfiguration",
          "Project path is required for project launch configuration");
      return;
    }

    _logger.LogInformation("Creating run session for project: {ProjectPath}", launchConfig.ProjectPath);

    try
    {
      // Use DI to resolve the handler per-request
      using var scope = _serviceProvider.CreateScope();
      var handler = scope.ServiceProvider.GetRequiredService<IRunSessionHandler>();

      var runSession = await handler.HandleCreateAsync(dcpId, launchConfig, payload.Env ?? [], _cts.Token);

      // Send notification to DCP
      await SendNotificationAsync(dcpId, new
      {
        notification_type = "processRestarted",
        session_id = runSession.RunId,
        dcp_id = dcpId,
        pid = runSession.ProcessId
      });

      _logger.LogInformation("RunSession processRestarted: {val}", JsonSerializer.Serialize(runSession, new JsonSerializerOptions()
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
      }));

      response.StatusCode = 201;
      response.Headers.Add("Location", $"http://localhost:{Port}/run_session/{runSession.RunId}");
      response.Close();

      _logger.LogInformation("Run session {RunId} created successfully", runSession.RunId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create run session for {ProjectPath}", launchConfig.ProjectPath);
      await SendErrorResponseAsync(response, 500, "SessionCreationFailed",
          $"Failed to create run session: {ex.Message}");
    }
  }

  private async Task HandleDeleteRunSessionAsync(string runId, HttpListenerResponse response)
  {
    _logger.LogInformation("Deleting run session {RunId}", runId);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var handler = scope.ServiceProvider.GetRequiredService<IRunSessionHandler>();

      await handler.HandleTerminateAsync(runId, _cts.Token);

      response.StatusCode = 200;
    }
    catch (KeyNotFoundException)
    {
      response.StatusCode = 404;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete run session {RunId}", runId);
      response.StatusCode = 500;
    }

    response.Close();
  }

  private async Task HandleWebSocketAsync(HttpListenerContext context)
  {
    var request = context.Request;
    var dcpId = request.Headers["Microsoft-Developer-DCP-Instance-ID"]!;
    _logger.LogInformation("WebSocket connection request for DCP ID: {DcpId}", dcpId);

    var wsContext = await context.AcceptWebSocketAsync(null);
    var webSocket = wsContext.WebSocket;

    _webSocketsByDcpId[dcpId] = webSocket;

    // Send any pending notifications
    if (_pendingNotificationsByDcpId.TryGetValue(dcpId, out var pendingQueue))
    {
      while (pendingQueue.Count > 0)
      {
        var notification = pendingQueue.Dequeue();
        await SendNotificationToWebSocketAsync(webSocket, notification);
      }
      _pendingNotificationsByDcpId.Remove(dcpId);
    }

    var buffer = new byte[1024 * 4];
    while (webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
    {
      try
      {
        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

        if (result.MessageType == WebSocketMessageType.Close)
        {
          await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
          break;
        }
      }
      catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error receiving WebSocket message");
        break;
      }
    }

    _webSocketsByDcpId.Remove(dcpId);
    _logger.LogInformation("WebSocket connection closed for DCP ID: {DcpId}", dcpId);
  }

  public async Task SendNotificationAsync(string dcpId, object notification, CancellationToken cancellationToken = default)
  {
    if (_webSocketsByDcpId.TryGetValue(dcpId, out var webSocket) && webSocket.State == WebSocketState.Open)
    {
      await SendNotificationToWebSocketAsync(webSocket, notification);
    }
    else
    {
      _logger.LogWarning("No WebSocket found for DCP ID: {DcpId}, queueing notification", dcpId);
      if (!_pendingNotificationsByDcpId.ContainsKey(dcpId))
      {
        _pendingNotificationsByDcpId[dcpId] = new Queue<object>();
      }
      _pendingNotificationsByDcpId[dcpId].Enqueue(notification);
    }
  }

  private async Task SendNotificationToWebSocketAsync(WebSocket webSocket, object notification)
  {
    var options = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    var json = JsonSerializer.Serialize(notification, options) + "\n";
    var bytes = Encoding.UTF8.GetBytes(json);
    await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
  }

  private async Task SendJsonResponseAsync(HttpListenerResponse response, int statusCode, object data)
  {
    response.StatusCode = statusCode;
    response.ContentType = "application/json";

    var options = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    var json = JsonSerializer.Serialize(data, options);
    var bytes = Encoding.UTF8.GetBytes(json);
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes);
    response.Close();
  }

  private async Task SendErrorResponseAsync(HttpListenerResponse response, int statusCode, string code, string message)
  {
    var error = new
    {
      error = new { code, message, details = Array.Empty<object>() }
    };
    await SendJsonResponseAsync(response, statusCode, error);
  }

  private static int GetFreePort()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
  }

  public async ValueTask DisposeAsync()
  {
    _cts.Cancel();

    foreach (var (dcpId, ws) in _webSocketsByDcpId.ToList())
    {
      try
      {
        if (ws.State == WebSocketState.Open)
        {
          await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
        }
        ws.Dispose();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error closing WebSocket for DCP ID {DcpId}", dcpId);
      }
    }

    _listener?.Stop();

    if (_listenerTask != null)
    {
      await _listenerTask;
    }

    _startLock.Dispose();
    _cts.Dispose();
  }
}