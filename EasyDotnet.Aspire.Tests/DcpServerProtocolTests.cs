using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using EasyDotnet.Aspire.Certificates;
using EasyDotnet.Aspire.Contracts;
using EasyDotnet.Aspire.RunSessionManager;
using Microsoft.Extensions.Logging.Abstractions;
using DcpServerImpl = EasyDotnet.Aspire.DcpServer.DcpServer;

namespace EasyDotnet.Aspire.Tests;

/// <summary>
/// Drives the real DCP server (Kestrel HTTPS + WS) over loopback, playing the role of DCP, to
/// prove the IDE-execution protocol: capability negotiation, that two <c>PUT /run_session</c>
/// requests start two projects through the editor callback, and that <c>DELETE</c> stops one and
/// emits a <c>sessionTerminated</c> notification.
/// </summary>
public class DcpServerProtocolTests
{
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
  private const string InstanceId = "aspireextrun1";
  private const string InstanceHeader = "Microsoft-Developer-DCP-Instance-ID";

  [Test]
  public async Task TwoRunSessions_StartTwoProjects_AndDeleteStopsOne()
  {
    var credentials = CertificateFactory.Create();
    var ide = new RecordingIde();
    var runSessions = new RunSessionManager.RunSessionManager(ide, NullLogger<RunSessionManager.RunSessionManager>.Instance);
    await using var server = new DcpServerImpl(credentials, runSessions, NullLoggerFactory.Instance);
    var connection = await server.StartAsync(CancellationToken.None);

    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{connection.Port}") };
    http.DefaultRequestHeaders.Authorization = new("Bearer", connection.Token);
    http.DefaultRequestHeaders.Add(InstanceHeader, InstanceId);

    // Capability negotiation.
    var info = await http.GetStringAsync("/info");
    await Assert.That(info).Contains(DcpProtocolVersions.V20240303);
    await Assert.That(info).Contains(LaunchConfigurationTypes.Project);

    // DCP subscribes to notifications before the first run session.
    using var ws = new ClientWebSocket();
    ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    ws.Options.SetRequestHeader("Authorization", $"Bearer {connection.Token}");
    ws.Options.SetRequestHeader(InstanceHeader, InstanceId);
    await ws.ConnectAsync(new Uri($"wss://localhost:{connection.Port}/run_session/notify"), CancellationToken.None);

    // Two project resources => two run sessions.
    var runIdA = await PutRunSessionAsync(http, "/work/ApiService/ApiService.csproj");
    var runIdB = await PutRunSessionAsync(http, "/work/Web/Web.csproj");

    await WaitForAsync(() => ide.Runs.Count == 2);
    await Assert.That(ide.Runs.Select(r => r.ProjectPath)).Contains("/work/ApiService/ApiService.csproj");
    await Assert.That(ide.Runs.Select(r => r.ProjectPath)).Contains("/work/Web/Web.csproj");

    // Stopping one run session terminates that project and notifies DCP.
    var delete = await http.DeleteAsync($"/run_session/{runIdA}");
    await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.OK);
    await WaitForAsync(() => ide.Stops.Contains(runIdA));

    var notification = await ReadNotificationAsync(ws);
    await Assert.That(notification).Contains(NotificationTypes.SessionTerminated);
    await Assert.That(notification).Contains(runIdA);
  }

  [Test]
  public async Task ReportProcessId_EmitsProcessRestartedNotification()
  {
    var credentials = CertificateFactory.Create();
    var ide = new RecordingIde();
    var runSessions = new RunSessionManager.RunSessionManager(ide, NullLogger<RunSessionManager.RunSessionManager>.Instance);
    await using var server = new DcpServerImpl(credentials, runSessions, NullLoggerFactory.Instance);
    var connection = await server.StartAsync(CancellationToken.None);

    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{connection.Port}") };
    http.DefaultRequestHeaders.Authorization = new("Bearer", connection.Token);
    http.DefaultRequestHeaders.Add(InstanceHeader, InstanceId);

    using var ws = new ClientWebSocket();
    ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    ws.Options.SetRequestHeader("Authorization", $"Bearer {connection.Token}");
    ws.Options.SetRequestHeader(InstanceHeader, InstanceId);
    await ws.ConnectAsync(new Uri($"wss://localhost:{connection.Port}/run_session/notify"), CancellationToken.None);

    var runId = await PutRunSessionAsync(http, "/work/ApiService/ApiService.csproj");
    await WaitForAsync(() => ide.Runs.Count == 1);

    await runSessions.ReportProcessIdAsync(runId, 4321);

    var notification = await ReadNotificationAsync(ws);
    await Assert.That(notification).Contains(NotificationTypes.ProcessRestarted);
    await Assert.That(notification).Contains(runId);
    await Assert.That(notification).Contains("4321");
  }

  [Test]
  public async Task Dispose_SendsGracefulWebSocketClose()
  {
    var credentials = CertificateFactory.Create();
    var runSessions = new RunSessionManager.RunSessionManager(new RecordingIde(), NullLogger<RunSessionManager.RunSessionManager>.Instance);
    var server = new DcpServerImpl(credentials, runSessions, NullLoggerFactory.Instance);
    var connection = await server.StartAsync(CancellationToken.None);

    using var ws = new ClientWebSocket();
    ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    ws.Options.SetRequestHeader("Authorization", $"Bearer {connection.Token}");
    ws.Options.SetRequestHeader(InstanceHeader, InstanceId);
    await ws.ConnectAsync(new Uri($"wss://localhost:{connection.Port}/run_session/notify"), CancellationToken.None);

    // Ensure the server has registered the socket (sink wired in HandleNotifyAsync) before shutting down.
    await WaitForAsync(() => runSessions.Notifications is not null);

    await server.DisposeAsync();

    var buffer = new byte[256];
    using var cts = new CancellationTokenSource(Timeout);
    var result = await ws.ReceiveAsync(buffer, cts.Token);
    await Assert.That(result.MessageType).IsEqualTo(WebSocketMessageType.Close);
    await Assert.That(result.CloseStatus).IsEqualTo(WebSocketCloseStatus.NormalClosure);
  }

  [Test]
  public async Task RunSession_WithoutBearerToken_IsUnauthorized()
  {
    var credentials = CertificateFactory.Create();
    var runSessions = new RunSessionManager.RunSessionManager(new RecordingIde(), NullLogger<RunSessionManager.RunSessionManager>.Instance);
    await using var server = new DcpServerImpl(credentials, runSessions, NullLoggerFactory.Instance);
    var connection = await server.StartAsync(CancellationToken.None);

    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{connection.Port}") };

    var response = await http.GetAsync("/info");
    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    // The error response carries a structured envelope DCP surfaces in the app-host log.
    var body = await response.Content.ReadAsStringAsync();
    await Assert.That(body).Contains("\"error\"");
    await Assert.That(body).Contains("MissingAuthorizationHeader");
  }

  [Test]
  public async Task RunSession_WithUnsupportedLaunchConfig_Returns400WithErrorBody()
  {
    var credentials = CertificateFactory.Create();
    var runSessions = new RunSessionManager.RunSessionManager(new RecordingIde(), NullLogger<RunSessionManager.RunSessionManager>.Instance);
    await using var server = new DcpServerImpl(credentials, runSessions, NullLoggerFactory.Instance);
    var connection = await server.StartAsync(CancellationToken.None);

    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{connection.Port}") };
    http.DefaultRequestHeaders.Authorization = new("Bearer", connection.Token);
    http.DefaultRequestHeaders.Add(InstanceHeader, InstanceId);

    var payload = new RunSessionPayload(
        LaunchConfigurations: [new LaunchConfiguration("python", null, LaunchModes.NoDebug, null, null)],
        Env: null,
        Args: null);
    using var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    var response = await http.PutAsync("/run_session", content);

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    var body = await response.Content.ReadAsStringAsync();
    await Assert.That(body).Contains("UnsupportedLaunchConfiguration");
  }

  [Test]
  public async Task RequestFromDifferentDcpInstance_IsRejected()
  {
    var credentials = CertificateFactory.Create();
    var runSessions = new RunSessionManager.RunSessionManager(new RecordingIde(), NullLogger<RunSessionManager.RunSessionManager>.Instance);
    await using var server = new DcpServerImpl(credentials, runSessions, NullLoggerFactory.Instance);
    var connection = await server.StartAsync(CancellationToken.None);

    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{connection.Port}") };
    http.DefaultRequestHeaders.Authorization = new("Bearer", connection.Token);

    // First instance binds the server to its context.
    http.DefaultRequestHeaders.Add(InstanceHeader, InstanceId);
    var first = await http.GetAsync("/info");
    await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);

    // A second DCP instance with the right token but a different instance-id is rejected.
    using var other = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{connection.Port}") };
    other.DefaultRequestHeaders.Authorization = new("Bearer", connection.Token);
    other.DefaultRequestHeaders.Add(InstanceHeader, "differentinst");
    var second = await other.GetAsync("/info");
    await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
  }

  [Test]
  public async Task RunSession_Mode_MapsToDebugFlag()
  {
    var credentials = CertificateFactory.Create();
    var ide = new RecordingIde();
    var runSessions = new RunSessionManager.RunSessionManager(ide, NullLogger<RunSessionManager.RunSessionManager>.Instance);
    await using var server = new DcpServerImpl(credentials, runSessions, NullLoggerFactory.Instance);
    var connection = await server.StartAsync(CancellationToken.None);

    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{connection.Port}") };
    http.DefaultRequestHeaders.Authorization = new("Bearer", connection.Token);
    http.DefaultRequestHeaders.Add(InstanceHeader, InstanceId);

    await PutRunSessionAsync(http, "/work/Api/Api.csproj", LaunchModes.Debug);
    await PutRunSessionAsync(http, "/work/Web/Web.csproj", LaunchModes.NoDebug);

    await WaitForAsync(() => ide.Runs.Count == 2);
    await Assert.That(ide.Runs.Single(r => r.ProjectPath.Contains("Api")).Debug).IsTrue();
    await Assert.That(ide.Runs.Single(r => r.ProjectPath.Contains("Web")).Debug).IsFalse();
  }

  private static async Task<string> PutRunSessionAsync(HttpClient http, string projectPath, string mode = LaunchModes.NoDebug)
  {
    var payload = new RunSessionPayload(
        LaunchConfigurations: [new LaunchConfiguration(LaunchConfigurationTypes.Project, projectPath, mode, null, null)],
        Env: [new EnvVar("ASPNETCORE_URLS", "http://localhost:0")],
        Args: null);

    var json = System.Text.Json.JsonSerializer.Serialize(payload);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");
    var response = await http.PutAsync("/run_session", content);

    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
    var location = response.Headers.Location?.ToString() ?? throw new InvalidOperationException("missing Location header");
    return location[(location.LastIndexOf('/') + 1)..];
  }

  private static async Task<string> ReadNotificationAsync(ClientWebSocket ws)
  {
    var buffer = new byte[8192];
    using var cts = new CancellationTokenSource(Timeout);
    var result = await ws.ReceiveAsync(buffer, cts.Token);
    return Encoding.UTF8.GetString(buffer, 0, result.Count);
  }

  private static async Task WaitForAsync(Func<bool> condition)
  {
    using var cts = new CancellationTokenSource(Timeout);
    while (!condition())
    {
      cts.Token.ThrowIfCancellationRequested();
      await Task.Delay(25, cts.Token);
    }
  }

  private sealed class RecordingIde : IIdeCallback
  {
    public ConcurrentQueue<RunManagedResourceRequest> Runs { get; } = new();
    public ConcurrentBag<string> Stops { get; } = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<int>> _exits = new();

    public Task<int> RunManagedResourceAsync(RunManagedResourceRequest request, CancellationToken ct)
    {
      Runs.Enqueue(request);
      var tcs = _exits.GetOrAdd(request.RunId, _ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously));
      return tcs.Task.WaitAsync(ct);
    }

    public Task StopManagedResourceAsync(string runId, CancellationToken ct)
    {
      Stops.Add(runId);
      if (_exits.TryGetValue(runId, out var tcs))
      {
        tcs.TrySetResult(0);
      }
      return Task.CompletedTask;
    }
  }
}