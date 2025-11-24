using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Infrastructure.Dap;
using EasyDotnet.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Aspire.Server;

public record EnvVar(string Name, string Value);

public sealed class DcpServer : IAsyncDisposable
{
  private readonly INetcoreDbgService _netcoreDbgService;
  private readonly ILogger<NetcoreDbgService> _logger;
  private readonly ILogger<DebuggerProxy> _debuggerProxyLogger;
  private readonly IClientService _clientService;
  private readonly IMsBuildService _msBuildService;
  private readonly HttpListener _listener;
  private readonly Dictionary<string, RunSession> _runSessions = [];
  private readonly Dictionary<string, WebSocket> _webSocketsByDcpId = [];
  private readonly Dictionary<string, Queue<object>> _pendingNotificationsByDcpId = [];
  private readonly CancellationTokenSource _cts = new();
  private readonly Dictionary<string, int> _debuggerSessionMap = [];
  //Store a dictionary of runId -> debuggerSessionId
  private Task? _listenerTask;

  public int Port { get; }
  public string Token { get; }

  private DcpServer(
      INetcoreDbgService netcoreDbgService,
      IClientService clientService,
      IMsBuildService msBuildService,
      string token,
      HttpListener listener,
      int port,
      ILogger<DebuggerProxy> debuggerProxyLogger,
      ILogger<NetcoreDbgService> logger
)
  {
    _netcoreDbgService = netcoreDbgService;
    _msBuildService = msBuildService;
    _clientService = clientService;
    Token = token;
    _listener = listener;
    Port = port;
    _debuggerProxyLogger = debuggerProxyLogger;
    _logger = logger;
  }

  public static async Task<DcpServer> CreateAsync(
      ILogger<DcpServer> logger,
      INetcoreDbgService netcoreDbgService,
      IMsBuildService msBuildService,
      IClientService clientService,
      ILogger<DebuggerProxy> debuggerProxyLogger,
      ILogger<NetcoreDbgService> logger2,
      CancellationToken cancellationToken = default)
  {
    var token = Guid.NewGuid().ToString("N");

    var port = GetFreePort();

    var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");

    listener.Start();

    var server = new DcpServer(netcoreDbgService, clientService, msBuildService, token, listener, port, debuggerProxyLogger, logger2);
    server.StartListening();

    logger.LogInformation("DCP server listening on port {Port}", port);

    return server;
  }

  private void StartListening()
  {
    _listenerTask = Task.Run(async () =>
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
  }

  private async Task HandleRequestAsync(HttpListenerContext context)
  {
    try
    {
      var request = context.Request;
      var response = context.Response;

      // Auth check (not required for /info endpoint per spec)
      var path = request.Url!.AbsolutePath;
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
        if (providedToken != Token)
        {
          await SendErrorResponseAsync(response, 401, "InvalidToken",
              "Invalid token in Authorization header.");
          return;
        }
      }

      // Route the request
      var method = request.HttpMethod;

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
      ProtocolsSupported = new[] { "2024-03-03", "2024-04-23", "2025-10-01" },
      SupportedLaunchConfigurations = new[] { "project", "prompting", "baseline.v1", "secret-prompts.v1", "ms-dotnettools.csharp", "devkit", "ms-dotnettools.csdevkit" }
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

    _logger.LogInformation("Received run session request: {Json}", JsonSerializer.Serialize(payload!.LaunchConfigurations, new JsonSerializerOptions() { WriteIndented = true }));

    if (payload == null || payload.LaunchConfigurations.Length == 0)
    {
      await SendErrorResponseAsync(response, 400, "InvalidPayload", "Invalid request payload");
      return;
    }

    var runId = GenerateRunId();
    _logger.LogInformation("Creating run session {RunId} for DCP ID {DcpId}", runId, dcpId);

    var launchConfig = payload.LaunchConfigurations[0];

    if (launchConfig is not ProjectLaunchConfiguration projectConfig)
    {
      await SendErrorResponseAsync(response, 400, "UnsupportedLaunchConfiguration",
          $"Unsupported launch configuration type: {launchConfig.Type}");
      return;
    }

    if (string.IsNullOrEmpty(projectConfig.ProjectPath))
    {
      await SendErrorResponseAsync(response, 400, "InvalidProjectConfiguration",
          "Project path is required for project launch configuration");
      return;
    }

    _logger.LogInformation("Project path: {ProjectPath}, Mode: {Mode}",
        projectConfig.ProjectPath, projectConfig.Mode);

    try
    {
      var project = await _msBuildService.GetOrSetProjectPropertiesAsync(
          projectConfig.ProjectPath, null, "Debug", CancellationToken.None);

      var debug = projectConfig.Mode != "NoDebug"; // Default is "Debug"

      int? debuggerPort = null;
      System.Diagnostics.Process? serviceProcess = null;

      if (!debug)
      {
        throw new InvalidOperationException("DCP does not support non-debugging");
      }

      var x = new NetcoreDbgService(_logger, _debuggerProxyLogger);
      var envVars = BuildEnvironmentVariables(payload);

      var binaryPath = _clientService.ClientOptions!.DebuggerOptions!.BinaryPath!;

      var port = await x.Start(
          binaryPath, project, projectConfig.ProjectPath, null, null, envVars);
      debuggerPort = port;

      _logger.LogInformation("Started debugger on port {Port} for {ProjectPath}", debuggerPort, projectConfig.ProjectPath);

      if (Path.GetFileName(projectConfig.ProjectPath) == "aspire.ApiService")
      {
        await _clientService.RequestSetBreakpoint("C:/Users/Gustav/repo/aspire/aspire.ApiService/Program.cs", 1);
      }

      if (Path.GetFileName(projectConfig.ProjectPath) == "aspire.Web")
      {
        await _clientService.RequestSetBreakpoint("C:/Users/Gustav/repo/aspire/aspire.Web/Program.cs", 1);
      }
      var id = await _clientService.RequestStartDebugSession("127.0.0.1", port);
      _debuggerSessionMap.Add(runId, id);

      // Send process restarted notification
      var pid = debug ? null : serviceProcess?.Id;
      await SendNotificationToDcpAsync(dcpId, new
      {
        notification_type = "processRestarted",
        session_id = runId,
        pid
      });

      _runSessions[runId] = new RunSession
      {
        RunId = runId,
        DcpId = dcpId,
        ProjectPath = projectConfig.ProjectPath,
        DebuggerPort = debuggerPort,
        IsDebug = debug,
        ServiceProcess = serviceProcess
      };

      response.StatusCode = 201;
      response.Headers.Add("Location", $"http://localhost:{Port}/run_session/{runId}");
      response.Close();

      _logger.LogInformation("Run session {RunId} created successfully for project {ProjectPath}", runId, projectConfig.ProjectPath);
      _logger.LogInformation("Run session http://localhost:{Port}/run_session/{runId}", port, runId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create run session {RunId}", runId);
      await SendErrorResponseAsync(response, 500, "DebugSessionFailed",
          $"Failed to start debug session: {ex.Message}");
    }
  }

  private async Task HandleDeleteRunSessionAsync(string runId, HttpListenerResponse response)
  {
    _logger.LogInformation("Deleting run session {RunId}", runId);


    if (_runSessions.TryGetValue(runId, out var session))
    {
      var success = _debuggerSessionMap.TryGetValue(runId, out var sessionId);
      if (success)
      {
        await _clientService.RequestTerminateDebugSession(sessionId);
      }
      _runSessions.Remove(runId);

      await SendNotificationToDcpAsync(session.DcpId, new
      {
        notification_type = "sessionTerminated",
        session_id = runId,
        exit_code = session.ServiceProcess?.ExitCode ?? 0
      });

      response.StatusCode = 200;
    }
    else
    {
      response.StatusCode = 404;
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
        await SendNotificationAsync(webSocket, notification);
      }
      _pendingNotificationsByDcpId.Remove(dcpId);
    }

    // Keep connection alive and handle pings (required by protocol 2024-04-23)
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
        else if (result.MessageType == WebSocketMessageType.Text)
        {
          // Handle any messages from DCP if needed
          var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
          _logger.LogDebug("Received WebSocket message from DCP: {Message}", message);
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
      error = new
      {
        code = code,
        message = message,
        details = Array.Empty<object>()
      }
    };
    await SendJsonResponseAsync(response, statusCode, error);
  }

  private async Task SendNotificationAsync(WebSocket webSocket, object notification)
  {
    var options = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    var json = JsonSerializer.Serialize(notification, options) + "\n"; // JSON Lines format

    var bytes = Encoding.UTF8.GetBytes(json);
    await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
  }

  public async Task SendNotificationToDcpAsync(string dcpId, object notification)
  {
    if (_webSocketsByDcpId.TryGetValue(dcpId, out var webSocket) && webSocket.State == WebSocketState.Open)
    {
      await SendNotificationAsync(webSocket, notification);
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

  private static int GetFreePort()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
  }


  private IDictionary<string, string> BuildEnvironmentVariables(RunSessionPayload payload)
  {
    var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (payload.Env == null || payload.Env.Length == 0)
    {
      _logger.LogWarning("No environment variables found in payload");
      return envVars;
    }

    _logger.LogInformation("Applying {Count} environment variables from payload", payload.Env.Length);

    // Check for ASPNETCORE_URLS specifically
    var aspnetcoreUrls = payload.Env.FirstOrDefault(e => e.Name.Equals("ASPNETCORE_URLS", StringComparison.OrdinalIgnoreCase));
    if (aspnetcoreUrls != null)
    {
      _logger.LogCritical("!!! ASPNETCORE_URLS = {Value} !!!", aspnetcoreUrls.Value);
    }
    else
    {
      _logger.LogWarning("!!! ASPNETCORE_URLS not found in env vars !!!");
    }

    // Log all URL- or PORT-related env vars
    foreach (var envVar in payload.Env.Where(e =>
        e.Name.Contains("URL", StringComparison.OrdinalIgnoreCase) ||
        e.Name.Contains("PORT", StringComparison.OrdinalIgnoreCase)))
    {
      _logger.LogInformation("  {Name} = {Value}", envVar.Name, envVar.Value);
    }

    // Populate dictionary
    foreach (var envVar in payload.Env)
    {
      if (string.IsNullOrWhiteSpace(envVar.Name))
        continue;

      envVars[envVar.Name] = envVar.Value ?? string.Empty;
    }

    return envVars;
  }

  private static string GenerateRunId() => $"run-{Guid.NewGuid():N}";

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
  }
}

// Supporting types matching the spec
public class RunSession
{
  public required string RunId { get; init; }
  public required string DcpId { get; init; }
  public required string ProjectPath { get; init; }
  public int? DebuggerPort { get; init; }
  public bool IsDebug { get; init; }
  public System.Diagnostics.Process? ServiceProcess { get; init; }
}

public class RunSessionPayload
{
  [JsonPropertyName("launch_configurations")]
  public LaunchConfiguration[] LaunchConfigurations { get; set; } = Array.Empty<LaunchConfiguration>();

  [JsonPropertyName("env")]
  public EnvVar[]? Env { get; set; }

  [JsonPropertyName("args")]
  public string[]? Args { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ProjectLaunchConfiguration), "project")]
public class LaunchConfiguration
{
  [JsonPropertyName("type")]
  public string Type { get; set; } = string.Empty;

  [JsonPropertyName("mode")]
  public string? Mode { get; set; } // "Debug" or "NoDebug"
}

public class ProjectLaunchConfiguration : LaunchConfiguration
{
  [JsonPropertyName("project_path")]
  public string ProjectPath { get; set; } = string.Empty;

  [JsonPropertyName("launch_profile")]
  public string? LaunchProfile { get; set; }

  [JsonPropertyName("disable_launch_profile")]
  public bool? DisableLaunchProfile { get; set; }
}