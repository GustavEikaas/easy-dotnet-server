using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Infrastructure.Aspire.Server.Controllers;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Aspire.Server;

public sealed class DcpServer : IAsyncDisposable
{
  private readonly ILogger<DcpServer> _logger;
  private readonly INetcoreDbgService _netcoreDbgService;
  private readonly IClientService _clientService;
  private readonly IMsBuildService _msBuildService;
  private readonly X509Certificate2 _certificate;
  private readonly HttpListener _listener;
  private readonly Dictionary<string, RunSession> _runSessions = [];
  private readonly Dictionary<string, WebSocket> _webSocketsByDcpId = [];
  private readonly Dictionary<string, Queue<object>> _pendingNotificationsByDcpId = [];
  private readonly CancellationTokenSource _cts = new();
  private Task? _listenerTask;

  public int Port { get; }
  public string Token { get; }
  public string CertificateBase64 { get; }

  private DcpServer(
      ILogger<DcpServer> logger,
      INetcoreDbgService netcoreDbgService,
      IClientService clientService,
      IMsBuildService msBuildService,
      string token,
      X509Certificate2 certificate,
      HttpListener listener,
      int port)
  {
    _logger = logger;
    _netcoreDbgService = netcoreDbgService;
    _msBuildService = msBuildService;
    _clientService = clientService;
    Token = token;
    _certificate = certificate;
    CertificateBase64 = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
    _listener = listener;
    Port = port;
  }

  public static async Task<DcpServer> CreateAsync(
      ILogger<DcpServer> logger,
      INetcoreDbgService netcoreDbgService,
      IMsBuildService msBuildService,
      IClientService clientService,
      CancellationToken cancellationToken = default)
  {
    var token = Guid.NewGuid().ToString("N");
    var certificate = GenerateSelfSignedCertificate();

    // Find a free port
    var port = GetFreePort();

    var listener = new HttpListener();
    // Note: For production, you'd need to bind the certificate using netsh
    // For now, we'll use HTTP (the spec says HTTPS is "strongly recommended" but optional)
    listener.Prefixes.Add($"http://localhost:{port}/");

    listener.Start();

    var server = new DcpServer(logger, netcoreDbgService, clientService, msBuildService, token, certificate, listener, port);
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
    var info = new
    {
      protocols_supported = new[] { "2024-03-03", "2024-04-23" },
      supported_launch_configurations = new[] { "project" }
    };
    await SendJsonResponseAsync(response, 200, info);
  }

  private async Task HandleCreateRunSessionAsync(HttpListenerRequest request, HttpListenerResponse response)
  {
    var dcpId = request.Headers["Microsoft-Developer-DCP-Instance-ID"]!;

    using var reader = new StreamReader(request.InputStream);
    var json = await reader.ReadToEndAsync();

    _logger.LogInformation("Received run session request: {Json}", json);

    var options = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
      Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    var payload = JsonSerializer.Deserialize<RunSessionPayload>(json, options);

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

      //TODO: revert
      var debug = true; // projectConfig.Mode != "NoDebug"; // Default is "Debug"

      int? debuggerPort = null;
      System.Diagnostics.Process? serviceProcess = null;

      if (debug)
      {
        // Start with debugger attached
        var binaryPath = "netcoredbg"; // TODO: get from config
        var port = await _netcoreDbgService.Start(
            binaryPath, project, projectConfig.ProjectPath, null, null);
        debuggerPort = port;

        _logger.LogInformation("Started debugger on port {Port} for {ProjectPath}", debuggerPort, projectConfig.ProjectPath);
        // await _clientService.RequestSetBreakpoint("C:/Users/Gustav/repo/aspire/aspire.ApiService/Program.cs", 1);
        await _clientService.RequestStartDebugSession("127.0.0.1", port);
      }
      else
      {
        // Start without debugger - just run the project directly
        _logger.LogInformation("Starting project without debugger: {ProjectPath}", projectConfig.ProjectPath);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
          FileName = "dotnet",
          Arguments = $"run --project \"{projectConfig.ProjectPath}\" --no-launch-profile",
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          WorkingDirectory = Path.GetDirectoryName(projectConfig.ProjectPath)!,
          CreateNoWindow = true
        };

        if (payload.Env != null)
        {
          _logger.LogInformation("Applying {Count} environment variables", payload.Env.Length);

          var aspnetcoreUrls = payload.Env.FirstOrDefault(e => e.Name == "ASPNETCORE_URLS");
          if (aspnetcoreUrls != null)
          {
            _logger.LogCritical("!!! ASPNETCORE_URLS = {Value} !!!", aspnetcoreUrls.Value);
          }
          else
          {
            _logger.LogWarning("!!! ASPNETCORE_URLS not found in env vars !!!");
          }

          // Log ALL env vars that contain URL or PORT
          foreach (var envVar in payload.Env.Where(e =>
              e.Name.Contains("URL", StringComparison.OrdinalIgnoreCase) ||
              e.Name.Contains("PORT", StringComparison.OrdinalIgnoreCase)))
          {
            _logger.LogInformation("  {Name} = {Value}", envVar.Name, envVar.Value);
          }

          foreach (var envVar in payload.Env)
          {
            psi.Environment[envVar.Name] = envVar.Value;
          }
        }


        // Apply arguments from the payload
        if (payload.Args != null && payload.Args.Length > 0)
        {
          psi.Arguments += " -- " + string.Join(" ", payload.Args);
        }

        serviceProcess = System.Diagnostics.Process.Start(psi);
        if (serviceProcess == null)
        {
          throw new Exception("Failed to start service process");
        }

        _logger.LogInformation("Started service process with PID {Pid}", serviceProcess.Id);

        // Monitor output
        serviceProcess.OutputDataReceived += (_, e) =>
        {
          if (!string.IsNullOrEmpty(e.Data))
          {
            _logger.LogInformation("[{ProjectName}] {Output}",
                Path.GetFileNameWithoutExtension(projectConfig.ProjectPath), e.Data);

            // Send service logs notification
            _ = SendNotificationToDcpAsync(dcpId, new
            {
              notification_type = "serviceLogs",
              session_id = runId,
              is_std_err = false,
              log_message = e.Data
            });
          }
        };

        serviceProcess.ErrorDataReceived += (_, e) =>
        {
          if (!string.IsNullOrEmpty(e.Data))
          {
            _logger.LogError("[{ProjectName}] {Error}",
                Path.GetFileNameWithoutExtension(projectConfig.ProjectPath), e.Data);

            // Send service logs notification
            _ = SendNotificationToDcpAsync(dcpId, new
            {
              notification_type = "serviceLogs",
              session_id = runId,
              is_std_err = true,
              log_message = e.Data
            });
          }
        };

        serviceProcess.BeginOutputReadLine();
        serviceProcess.BeginErrorReadLine();

        // Monitor process exit
        serviceProcess.EnableRaisingEvents = true;
        serviceProcess.Exited += async (_, _) =>
        {
          _logger.LogInformation("Service process {Pid} exited with code {ExitCode}",
              serviceProcess.Id, serviceProcess.ExitCode);

          // Send session terminated notification
          await SendNotificationToDcpAsync(dcpId, new
          {
            notification_type = "sessionTerminated",
            session_id = runId,
            exit_code = serviceProcess.ExitCode
          });
        };
      }

      // Send process restarted notification
      var pid = debug ? (int?)null : serviceProcess?.Id;
      await SendNotificationToDcpAsync(dcpId, new
      {
        notification_type = "processRestarted",
        session_id = runId,
        pid = pid
      });

      var session = new RunSession
      {
        RunId = runId,
        DcpId = dcpId,
        ProjectPath = projectConfig.ProjectPath,
        DebuggerPort = debuggerPort,
        IsDebug = debug,
        ServiceProcess = serviceProcess
      };

      _runSessions[runId] = session;

      response.StatusCode = 201;
      response.Headers.Add("Location", $"http://localhost:{Port}/run_session/{runId}");
      response.Close();

      _logger.LogInformation("Run session {RunId} created successfully for project {ProjectPath}",
          runId, projectConfig.ProjectPath);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create run session {RunId}", runId);
      await SendErrorResponseAsync(response, 500, "DebugSessionFailed",
          $"Failed to start debug session: {ex.Message}");
    }
  }

  private Task HandleDeleteRunSessionAsync(string runId, HttpListenerResponse response)
  {
    _logger.LogInformation("Deleting run session {RunId}", runId);

    if (_runSessions.TryGetValue(runId, out var session))
    {
      // Stop the service process if running
      if (session.ServiceProcess != null && !session.ServiceProcess.HasExited)
      {
        _logger.LogInformation("Killing service process {Pid}", session.ServiceProcess.Id);
        session.ServiceProcess.Kill();
      }

      _runSessions.Remove(runId);

      // Send session terminated notification
      _ = SendNotificationToDcpAsync(session.DcpId, new
      {
        notification_type = "sessionTerminated",
        session_id = runId,
        exit_code = session.ServiceProcess?.ExitCode ?? 0
      });

      response.StatusCode = 200;
    }
    else
    {
      response.StatusCode = 204;
    }

    response.Close();
    return Task.CompletedTask;
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

  private static X509Certificate2 GenerateSelfSignedCertificate()
  {
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    request.CertificateExtensions.Add(
        new X509BasicConstraintsExtension(false, false, 0, false));

    request.CertificateExtensions.Add(
        new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

    // CRITICAL: Must include SubjectAlternativeName per spec
    var sanBuilder = new SubjectAlternativeNameBuilder();
    sanBuilder.AddDnsName("localhost");
    sanBuilder.AddIpAddress(IPAddress.Loopback);
    request.CertificateExtensions.Add(sanBuilder.Build());

    var cert = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
    return new X509Certificate2(cert.Export(X509ContentType.Pfx));
  }

  private static int GetFreePort()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
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
    _certificate?.Dispose();

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