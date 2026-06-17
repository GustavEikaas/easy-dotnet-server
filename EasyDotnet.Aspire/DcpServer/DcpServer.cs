using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EasyDotnet.Aspire.Certificates;
using EasyDotnet.Aspire.Contracts;
using EasyDotnet.Aspire.RunSessionManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Aspire.DcpServer;

/// <summary>Connection info handed to the AppHost via <c>DEBUG_SESSION_*</c> env vars.</summary>
public sealed record DcpServerConnectionInfo(int Port, string Token, string CertificateBase64, string InfoJson);

/// <summary>
/// Kestrel-hosted implementation of the DCP IDE-execution endpoint
/// (aspire/docs/specs/IDE-execution.md): <c>GET /info</c>,
/// <c>PUT/DELETE /run_session</c>, and the <c>GET /run_session/notify</c> WebSocket.
/// HTTPS only, secured by the per-session bearer token.
/// </summary>
public sealed class DcpServer(DcpCredentials credentials, RunSessionManager.RunSessionManager runSessions, ILoggerFactory loggerFactory)
  : INotificationSink, IAsyncDisposable
{
  private static readonly IdeEndpointInfo Info = new(
      ProtocolsSupported: [DcpProtocolVersions.V20240303],
      SupportedLaunchConfigurations: [LaunchConfigurationTypes.Project]);

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly ILogger _log = loggerFactory.CreateLogger<DcpServer>();
  private WebApplication? _app;
  private WebSocket? _notifySocket;
  private readonly SemaphoreSlim _sendLock = new(1, 1);
  private string? _boundInstanceId;
  private int _versionWarned;

  public async Task<DcpServerConnectionInfo> StartAsync(CancellationToken ct)
  {
    var builder = WebApplication.CreateSlimBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(o =>
        o.Listen(IPAddress.Loopback, 0, lo => lo.UseHttps(credentials.ServerCertificate)));

    var app = builder.Build();
    app.UseWebSockets();

    app.Use(async (ctx, next) =>
    {
      if (!TryAuthorize(ctx, out var error))
      {
        await WriteErrorAsync(ctx, StatusCodes.Status401Unauthorized, error, ctx.RequestAborted);
        return;
      }
      WarnIfNewerApiVersion(ctx);
      await next();
    });

    app.MapGet("/info", () => Results.Json(Info, JsonOptions));
    app.MapGet("/run_session/notify", HandleNotifyAsync);
    app.MapPut("/run_session", HandleCreateSessionAsync);
    app.MapDelete("/run_session/{id}", HandleStopSessionAsync);

    await app.StartAsync(ct);
    _app = app;

    var port = ResolvePort(app);
    _log.LogInformation("DCP server listening on https://localhost:{Port}", port);
    return new DcpServerConnectionInfo(port, credentials.Token, credentials.CertificateBase64, JsonSerializer.Serialize(Info, JsonOptions));
  }

  // DCP appends ?api-version=<date> to every request except /info, and auto-downgrades to the
  // newest version we advertise in /info. A newer requested version is therefore harmless, but
  // worth surfacing once: it signals the AppHost's DCP speaks a protocol we haven't caught up to.
  // Versions are YYYY-MM-DD, so ordinal comparison matches chronological order.
  private void WarnIfNewerApiVersion(HttpContext ctx)
  {
    var apiVersion = ctx.Request.Query["api-version"].ToString();
    if (string.IsNullOrEmpty(apiVersion)
        || string.CompareOrdinal(apiVersion, DcpProtocolVersions.V20240303) <= 0)
    {
      return;
    }
    if (Interlocked.Exchange(ref _versionWarned, 1) == 0)
    {
      _log.LogWarning(
          "DCP requested api-version {Requested}, newer than the supported {Supported}. "
          + "DCP auto-downgrades, but the IDE-execution protocol support may be out of date.",
          apiVersion, DcpProtocolVersions.V20240303);
    }
  }

  private bool TryAuthorize(HttpContext ctx, out ErrorDetail error)
  {
    var header = ctx.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (string.IsNullOrEmpty(header))
    {
      error = new("MissingAuthorizationHeader", "An Authorization header is required.");
      return false;
    }
    if (!header.StartsWith(prefix, StringComparison.Ordinal))
    {
      error = new("InvalidAuthorizationHeader", "The Authorization header must use the 'Bearer' scheme.");
      return false;
    }
    if (!CryptographicEquals(header[prefix.Length..], credentials.Token))
    {
      error = new("InvalidToken", "Invalid or missing session token.");
      return false;
    }

    var instanceId = ctx.Request.Headers["Microsoft-Developer-DCP-Instance-ID"].ToString();
    if (string.IsNullOrEmpty(instanceId))
    {
      error = new("MissingInstanceId", "The Microsoft-Developer-DCP-Instance-ID header is required.");
      return false;
    }
    var bound = Interlocked.CompareExchange(ref _boundInstanceId, instanceId, null) ?? instanceId;
    if (!string.Equals(bound, instanceId, StringComparison.Ordinal))
    {
      error = new("InstanceIdMismatch", "This DCP server is bound to a different DCP instance.");
      return false;
    }

    error = null!;
    return true;
  }

  private static async Task WriteErrorAsync(HttpContext ctx, int statusCode, ErrorDetail error, CancellationToken ct)
  {
    ctx.Response.StatusCode = statusCode;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(JsonSerializer.Serialize(new ErrorResponse(error), JsonOptions), ct);
  }

  private static bool CryptographicEquals(string a, string b)
  {
    var ba = Encoding.UTF8.GetBytes(a);
    var bb = Encoding.UTF8.GetBytes(b);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
  }

  private async Task HandleNotifyAsync(HttpContext ctx)
  {
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
      ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
      return;
    }

    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    _notifySocket = ws;
    _log.LogInformation("DCP subscribed to run_session notifications");
    runSessions.Notifications = this;

    var buffer = new byte[4096];
    try
    {
      while (ws.State == WebSocketState.Open)
      {
        var result = await ws.ReceiveAsync(buffer, ctx.RequestAborted);
        if (result.MessageType == WebSocketMessageType.Close)
        {
          break;
        }
      }
    }
    catch (OperationCanceledException) { }
    catch (WebSocketException) { }
  }

  private async Task HandleCreateSessionAsync(HttpContext ctx)
  {
    RunSessionPayload? payload;
    try
    {
      payload = await JsonSerializer.DeserializeAsync<RunSessionPayload>(ctx.Request.Body, JsonOptions, ctx.RequestAborted);
    }
    catch (JsonException ex)
    {
      await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, new("InvalidRunSessionPayload", $"Request body is not valid JSON: {ex.Message}"), ctx.RequestAborted);
      return;
    }

    if (payload is null)
    {
      await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, new("InvalidRunSessionPayload", "The run_session request body was empty."), ctx.RequestAborted);
      return;
    }

    string runId;
    try
    {
      runId = runSessions.CreateRunSession(payload);
    }
    catch (DcpRunSessionException ex)
    {
      await WriteErrorAsync(ctx, StatusCodes.Status400BadRequest, new(ex.Code, ex.Message), ctx.RequestAborted);
      return;
    }
    catch (Exception ex)
    {
      await WriteErrorAsync(ctx, StatusCodes.Status500InternalServerError, new("UnexpectedError", ex.Message), ctx.RequestAborted);
      return;
    }

    ctx.Response.Headers.Location = $"https://localhost:{ResolvePort(_app!)}/run_session/{runId}";
    ctx.Response.StatusCode = StatusCodes.Status201Created;
  }

  private async Task<IResult> HandleStopSessionAsync(string id, CancellationToken ct)
  {
    var stopped = await runSessions.StopRunSessionAsync(id, ct);
    return stopped ? Results.Ok() : Results.NoContent();
  }

  public async Task SendAsync(object notification, CancellationToken ct)
  {
    var ws = _notifySocket;
    if (ws is not { State: WebSocketState.Open })
    {
      _log.LogDebug("Dropping notification; notify socket not connected");
      return;
    }

    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notification, notification.GetType(), JsonOptions) + "\n");

    try
    {
      await _sendLock.WaitAsync(ct);
    }
    catch (ObjectDisposedException)
    {
      return;
    }

    try
    {
      if (ws.State == WebSocketState.Open)
      {
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
      }
    }
    catch (WebSocketException ex)
    {
      _log.LogDebug(ex, "Notification not delivered (connection closing)");
    }
    finally
    {
      _sendLock.Release();
    }
  }

  private static int ResolvePort(WebApplication app)
  {
    var address = app.Urls.FirstOrDefault()
        ?? throw new InvalidOperationException("DCP server has no bound address");
    return new Uri(address).Port;
  }

  public async ValueTask DisposeAsync()
  {
    await CloseNotifySocketAsync();

    credentials.ServerCertificate.Dispose();
    if (_app is not null)
    {
      await _app.DisposeAsync();
    }
    _sendLock.Dispose();
  }

  private async Task CloseNotifySocketAsync()
  {
    var ws = _notifySocket;
    if (ws is not { State: WebSocketState.Open })
    {
      return;
    }

    await _sendLock.WaitAsync();
    try
    {
      if (ws.State == WebSocketState.Open)
      {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "IDE endpoint shutting down", cts.Token);
      }
    }
    catch (Exception ex)
    {
      _log.LogDebug(ex, "Error closing notify socket");
    }
    finally
    {
      _sendLock.Release();
    }
  }
}