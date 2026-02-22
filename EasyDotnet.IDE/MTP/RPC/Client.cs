using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EasyDotnet.MTP;
using EasyDotnet.MTP.RPC.Models;
using EasyDotnet.MTP.RPC.Requests;
using EasyDotnet.MTP.RPC.Response;
using StreamJsonRpc;

namespace EasyDotnet.IDE.MTP.RPC;

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

  public IAsyncEnumerable<TestNodeUpdate> DiscoverTestsAsync(CancellationToken cancellationToken = default)
  {
    var runId = Guid.NewGuid();
    return StreamRpcAsync("testing/discoverTests", new DiscoveryRequest(runId), runId, cancellationToken);
  }

  public async IAsyncEnumerable<TestNodeUpdate> RunTestsAsync(RunRequestNode[] filter, [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var runId = Guid.NewGuid();

    await foreach (var node in StreamRpcAsync("testing/runTests", new RunRequest(filter, runId), runId, cancellationToken))
    {
      if (node.Node.ExecutionState != "in-progress" && node.Node.ExecutionState != "discovered")
      {
        yield return node;
      }
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
      catch (Exception ex)
      {
        channel.Writer.TryComplete(ex);
      }
      finally
      {
        channel.Writer.TryComplete();
        _server.RemoveStreamListener(runId);
      }
    }, cancellationToken);

    await using var _ = cancellationToken.Register(() =>
    {
      _server.RemoveStreamListener(runId);
      channel.Writer.TryComplete();
    });

    await foreach (var node in channel.Reader.ReadAllAsync(cancellationToken))
    {
      yield return node;
    }

    await rpcTask;
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