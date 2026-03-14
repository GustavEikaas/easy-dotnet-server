using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Models;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Requests;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Response;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC;

public sealed class MtpClient : IAsyncDisposable
{
  private readonly JsonRpc _jsonRpc;
  private readonly TcpClient _tcpClient;
  private readonly IProcessHandle _processHandle;
  private readonly MtpServer _server;
  private readonly ILogger<MtpClient> _logger;
  public readonly int DebugeeProcessId;

  private int _killedOrDisposed;

  private MtpClient(
      JsonRpc jsonRpc,
      TcpClient tcpClient,
      IProcessHandle processHandle,
      MtpServer server,
      ILogger<MtpClient> logger,
      int debugeeProcessId)
  {
    _jsonRpc = jsonRpc;
    _tcpClient = tcpClient;
    _processHandle = processHandle;
    _server = server;
    _logger = logger;
    DebugeeProcessId = debugeeProcessId;
  }

  internal static async Task<MtpClient> CreateAsync(
      string testExePath,
      ILogger<MtpClient> logger,
      MtpServer server,
      SourceLevels traceLevel,
      CancellationToken ct = default)
  {
    var tcpListener = new TcpListener(IPAddress.Loopback, 0);
    tcpListener.Start();

    var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
    logger.LogDebug("MTP listening on port {Port} for {Exe}", port, testExePath);

    var exeName = Path.GetFileNameWithoutExtension(testExePath);

    var processConfig = new ProcessConfiguration(testExePath)
    {
      Arguments = $"--server --client-host localhost --client-port {port} --diagnostic --diagnostic-verbosity trace",
      OnStandardOutput = (_, output) => logger.LogTrace("[{Exe}] {Output}", exeName, output),
      OnErrorOutput = (_, output) => logger.LogWarning("[{Exe}] stderr: {Output}", exeName, output),
      OnExit = (_, exitCode) =>
      {
        if (exitCode != 0)
          logger.LogWarning("[{Exe}] exited with code {ExitCode}", exeName, exitCode);
      }
    };

    var processHandle = ProcessFactory.Start(processConfig, false);

    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    connectCts.CancelAfter(TimeSpan.FromSeconds(60));

    var tcpClient = await tcpListener.AcceptTcpClientAsync(connectCts.Token);
    logger.LogDebug("[{Exe}] TCP connected", exeName);

    var stream = tcpClient.GetStream();
    var jsonRpc = new JsonRpc(stream);

    jsonRpc.TraceSource.Switch.Level = traceLevel;
    jsonRpc.TraceSource.Listeners.Clear();
    jsonRpc.TraceSource.Listeners.Add(new JsonRpcLogger(logger, $"MTP:{exeName}"));

    jsonRpc.AddLocalRpcTarget(server, new JsonRpcTargetOptions { MethodNameTransform = CommonMethodNameTransforms.CamelCase });
    jsonRpc.StartListening();

    var res = await jsonRpc.InvokeWithParameterObjectAsync<InitializeResponse>(
      "initialize",
      new InitializeRequest(
        ProcessId: Environment.ProcessId,
        ClientInfo: new(Name: "easy-dotnet"),
        Capabilities: new(Testing: new(DebuggerProvider: true)))
    );

    return new MtpClient(jsonRpc, tcpClient, processHandle, server, logger, res.ProcessId ?? processHandle.Id);
  }

  public IAsyncEnumerable<TestNodeUpdate> DiscoverTestsAsync(CancellationToken cancellationToken = default)
  {
    var runId = Guid.NewGuid();
    return StreamRpcAsync("testing/discoverTests", new DiscoveryRequest(runId), runId, cancellationToken);
  }

  public async IAsyncEnumerable<TestNodeUpdate> RunTestsAsync(
      RunRequestNode[] filter,
      [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var runId = Guid.NewGuid();

    await foreach (var node in StreamRpcAsync("testing/runTests", new RunRequest(filter, runId), runId, cancellationToken))
    {
      if (node.Node.ExecutionState != "in-progress" && node.Node.ExecutionState != "discovered")
        yield return node;
    }
  }

  private async IAsyncEnumerable<TestNodeUpdate> StreamRpcAsync(
      string rpcMethod,
      object requestObject,
      Guid runId,
      [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var channel = Channel.CreateUnbounded<TestNodeUpdate>();
    _server.RegisterStreamListener(runId, channel.Writer);

    var rpcTask = Task.Run(async () =>
    {
      try
      {
        await _jsonRpc.InvokeWithParameterObjectAsync<DiscoveryResponse>(rpcMethod, requestObject, cancellationToken);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        if (MtpRunHandling.IsDisconnectException(ex))
          _logger.LogWarning(ex, "MTP RPC {Method} disconnected for run {RunId}", rpcMethod, runId);
        else
          _logger.LogError(ex, "MTP RPC {Method} faulted for run {RunId}", rpcMethod, runId);

        channel.Writer.TryComplete(ex);
      }
      finally
      {
        channel.Writer.TryComplete();
        _server.RemoveStreamListener(runId);
      }
    });

    await using var registration = cancellationToken.Register(async () =>
    {
      try
      {
        _logger.LogDebug("Sending cancelTestRun for {RunId}", runId);
        await _jsonRpc.NotifyWithParameterObjectAsync("testing/cancelTestRun", new { runId });
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "cancelTestRun failed for {RunId}", runId);
      }
      finally
      {
        _server.RemoveStreamListener(runId);
        channel.Writer.TryComplete();
      }
    });

    await foreach (var node in channel.Reader.ReadAllAsync(CancellationToken.None))
      yield return node;

    if (cancellationToken.IsCancellationRequested)
    {
      _ = rpcTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
      yield break;
    }

    try { await rpcTask; }
    catch (OperationCanceledException) { }
  }

  public Task KillAsync()
  {
    if (Interlocked.Exchange(ref _killedOrDisposed, 1) == 1) return Task.CompletedTask;

    try { _jsonRpc.Dispose(); } catch { }
    try { _tcpClient.Dispose(); } catch { }
    try { _processHandle.Kill(); } catch { }

    return Task.CompletedTask;
  }

  public async ValueTask DisposeAsync()
  {
    try { await _jsonRpc.NotifyWithParameterObjectAsync("exit", new object()); }
    catch (Exception ex) { _logger.LogWarning(ex, "MTP exit notification failed"); }

    await KillAsync();

    try { await Task.WhenAny(_processHandle.WaitForExitAsync(), Task.Delay(2000)); }
    catch { }

    try { _processHandle.Dispose(); } catch { }

    GC.SuppressFinalize(this);
  }
}