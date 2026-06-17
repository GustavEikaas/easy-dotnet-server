using System.Globalization;
using EasyDotnet.Aspire.Certificates;
using EasyDotnet.Aspire.Contracts;
using EasyDotnet.Aspire.RunSessionManager;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.Aspire;

/// <summary>
/// JSON-RPC target the IDE drives. <c>aspire/launch</c> stands up a DCP server and
/// launches the AppHost so DCP connects back via <c>DEBUG_SESSION_*</c>.
/// </summary>
public sealed class AspireServer(IIdeCallback ide, ILoggerFactory loggerFactory) : IAsyncDisposable
{
  private readonly ILogger _log = loggerFactory.CreateLogger<AspireServer>();
  private DcpServer.DcpServer? _dcp;
  private RunSessionManager.RunSessionManager? _runSessions;
  private int _active; // 0 = idle, 1 = an Aspire app is running

  [JsonRpcMethod(AspireRpcMethods.Launch, UseSingleObjectParameterDeserialization = true)]
  public async Task<LaunchAppHostResponse> LaunchAsync(LaunchAppHostRequest request, CancellationToken ct)
  {
    // Strict singleton: there can never be more than one DCP server. If an Aspire app is
    // already running on this host, refuse hard rather than clobbering the live one.
    if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
    {
      throw new InvalidOperationException(
        "An Aspire app is already running; stop it before starting another. Only one DCP server can be active at a time.");
    }

    try
    {
      var credentials = CertificateFactory.Create();
      var runSessions = new RunSessionManager.RunSessionManager(ide, loggerFactory.CreateLogger<RunSessionManager.RunSessionManager>());
      _runSessions = runSessions;
      _dcp = new DcpServer.DcpServer(credentials, runSessions, loggerFactory);

      var connection = await _dcp.StartAsync(ct);
      _log.LogInformation("Launching AppHost {Project} against DCP endpoint :{Port}", request.AppHostProjectPath, connection.Port);

      var env = new Dictionary<string, string>(request.EnvironmentVariables ?? []);
      // host:port form (e.g. "localhost:36593"), matching the reference extension. DCP also
      // accepts a bare port, but Aspire's dashboard env handler requires the host:port form.
      env["DEBUG_SESSION_PORT"] = "localhost:" + connection.Port.ToString(CultureInfo.InvariantCulture);
      env["DEBUG_SESSION_TOKEN"] = connection.Token;
      env["DEBUG_SESSION_SERVER_CERTIFICATE"] = connection.CertificateBase64;
      env["DEBUG_SESSION_INFO"] = connection.InfoJson;
      // Tells DCP whether to request Debug mode for resources (resources only; the AppHost
      // itself still runs as a managed process either way).
      env["DEBUG_SESSION_RUN_MODE"] = request.Debug ? LaunchModes.Debug : LaunchModes.NoDebug;

      // The AppHost is relayed through the same IDE run path as any resource: the IDE
      // resolves the executable from the project and injects the DEBUG_SESSION_* env.
      var appHostRun = new RunManagedResourceRequest(
          RunId: AspireRunIds.AppHost,
          ProjectPath: request.AppHostProjectPath,
          Args: null,
          EnvironmentVariables: env);

      // The AppHost run blocks until the AppHost exits; that span is the lifetime of the
      // whole aspire run. When it ends (e.g. workspace/stop kills it), tear down the DCP
      // server so a subsequent launch starts clean.
      _ = RunAppHostAsync(appHostRun);
      return new LaunchAppHostResponse(true);
    }
    catch
    {
      // Launch failed before the AppHost run was handed off — release the singleton slot.
      await ShutdownAsync();
      throw;
    }
  }

  private async Task RunAppHostAsync(RunManagedResourceRequest appHostRun)
  {
    try
    {
      await ide.RunManagedResourceAsync(appHostRun, CancellationToken.None);
    }
    catch (Exception ex)
    {
      _log.LogWarning(ex, "AppHost run ended with an error");
    }
    finally
    {
      await ShutdownAsync();
    }
  }

  [JsonRpcMethod(AspireRpcMethods.ReportProcessId, UseSingleObjectParameterDeserialization = true)]
  public Task ReportProcessId(ReportProcessIdRequest request) =>
      _runSessions?.ReportProcessIdAsync(request.RunId, request.Pid) ?? Task.CompletedTask;

  [JsonRpcMethod(AspireRpcMethods.Shutdown)]
  public async Task ShutdownAsync()
  {
    // Stop child resources before tearing down: when the AppHost exits, DCP is gone and
    // can no longer reap them, so the managed resource processes would otherwise be orphaned.
    var runSessions = _runSessions;
    if (runSessions is not null)
    {
      await runSessions.StopAllAsync(CancellationToken.None);
    }
    if (_dcp is not null)
    {
      await _dcp.DisposeAsync();
      _dcp = null;
    }
    _runSessions = null;
    Interlocked.Exchange(ref _active, 0);
  }

  public async ValueTask DisposeAsync() => await ShutdownAsync();
}