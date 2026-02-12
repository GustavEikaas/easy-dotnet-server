using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.IDE.MTP;
using EasyDotnet.MTP.RPC.Models;
using EasyDotnet.MTP.RPC.Requests;
using EasyDotnet.MTP.RPC.Response;
using StreamJsonRpc;

namespace EasyDotnet.MTP.RPC;

public class Client : IAsyncDisposable
{
  private readonly JsonRpc _jsonRpc;
  private readonly TcpClient _tcpClient;
  private readonly IProcessHandle _processHandle;
  private readonly MtpServer _server;
  public readonly int DebugeeProcessId;

  private Client(JsonRpc jsonRpc, TcpClient tcpClient, IProcessHandle processHandle, MtpServer server, int debugeeProcessId)
  {
    _jsonRpc = jsonRpc;
    _tcpClient = tcpClient;
    _processHandle = processHandle;
    _server = server;
    DebugeeProcessId = debugeeProcessId;
  }

  public static async Task<Client> CreateAsync(string testExePath, bool debug = false)
  {
    var tcpListener = new TcpListener(IPAddress.Loopback, 0);
    tcpListener.Start();

    var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
    Console.WriteLine($"Listening on port: {port}");

    var server = new MtpServer();

    var processConfig = new ProcessConfiguration(testExePath)
    {
      Arguments = $"--server --client-host localhost --client-port {port} --diagnostic --diagnostic-verbosity trace",

      OnStandardOutput = (_, output) =>
      {
        if (debug)
        {
          Console.WriteLine(output);
        }
      },
      OnErrorOutput = (_, output) => Console.Error.WriteLine(output),
      OnExit = (_, exitCode) =>
      {
        if (exitCode == 0)
        {
          return;
        }
        // Console.Error.WriteLine($"[{testExePath}]: exit code '{exitCode}'");
      }
    };

    var processHandle = ProcessFactory.Start(processConfig, false);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var tcpClient = await tcpListener.AcceptTcpClientAsync(cts.Token);

    Console.WriteLine("Client connected");

    var stream = tcpClient.GetStream();
    var jsonRpc = new JsonRpc(stream);

    if (debug)
    {
      var ts = jsonRpc.TraceSource;
      ts.Switch.Level = SourceLevels.Verbose;
      ts.Listeners.Add(new ConsoleTraceListener());
    }

    jsonRpc.AddLocalRpcTarget(server, new JsonRpcTargetOptions { MethodNameTransform = CommonMethodNameTransforms.CamelCase });
    jsonRpc.StartListening();

    var res = await jsonRpc.InvokeWithParameterObjectAsync<InitializeResponse>(
      "initialize",
      new InitializeRequest(Environment.ProcessId, new("easy-dotnet"), new(new(DebuggerProvider: true)))
    );

    return new Client(jsonRpc, tcpClient, processHandle, server, res.ProcessId ?? processHandle.Id);
  }

  public async Task<TestNodeUpdate[]> DiscoverTestsAsync(CancellationToken cancellationToken = default)
  {
    var runId = Guid.NewGuid();

    return await WithCancellation(
           runId,
           () => _jsonRpc.InvokeWithParameterObjectAsync<DiscoveryResponse>(
               "testing/discoverTests", new DiscoveryRequest(runId), cancellationToken),
           cancellationToken
       );
  }

  public async Task<TestNodeUpdate[]> RunTestsAsync(RunRequestNode[] filter, CancellationToken cancellationToken)
  {
    var runId = Guid.NewGuid();

    var tests = await WithCancellation(
           runId,
           () => _jsonRpc.InvokeWithParameterObjectAsync<DiscoveryResponse>(
               "testing/runTests", new RunRequest(filter, runId), cancellationToken),
           cancellationToken
       );

    return [.. tests.Where(x => x.Node.ExecutionState != "in-progress" && x.Node.ExecutionState != "discovered")];
  }

  private async Task<TestNodeUpdate[]> WithCancellation(
      Guid runId,
      Func<Task> invokeRpcAsync,
      CancellationToken cancellationToken)
  {
    var tcs = new TaskCompletionSource<TestNodeUpdate[]>(TaskCreationOptions.RunContinuationsAsynchronously);

    _server.RegisterResponseListener(runId, tcs);

    using (cancellationToken.Register(() =>
    {
      tcs.TrySetCanceled(cancellationToken);
      _server.RemoveResponseListener(runId);
    }))
    {
      try
      {
        await invokeRpcAsync();
        return await tcs.Task;
      }
      catch
      {
        _server.RemoveResponseListener(runId);
        throw;
      }
    }
  }

  public async ValueTask DisposeAsync()
  {
    await _jsonRpc.NotifyWithParameterObjectAsync("exit", new object());
    _jsonRpc.Dispose();
    _tcpClient.Dispose();
    _processHandle.WaitForExit();
    _processHandle.Dispose();
    GC.SuppressFinalize(this);
  }

}