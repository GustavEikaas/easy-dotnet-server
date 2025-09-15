using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;

namespace EasyDotnet.Services;

public class NetcoreDbgService(ILogger<NetcoreDbgService> logger)
{

  public Process StartProcess()
  {
    var process = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = "netcoredbg",
        Arguments = "--interpreter=vscode", // force DAP mode
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      }
    };
    process.Start();
    return process;
  }


  public void Start()
  {
    var listener = new TcpListener(IPAddress.Any, 8086);
    var process = StartProcess();
    listener.Start();

    Task.Run(async () =>
    {
      Console.WriteLine("Waiting for client...");
      var client = await listener.AcceptTcpClientAsync();
      Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");

      await HandleClientAsync(client, process);
    });

  }

  private async Task HandleClientAsync(TcpClient client, Process process)
  {
    using (client)
    {
      var clientStream = client.GetStream();
      // var buffer = new byte[8192];
      // while (true)
      // {
      //   var read = await clientStream.ReadAsync(buffer, 0, buffer.Length);
      //   if (read == 0)
      //   {
      //     Console.WriteLine("Client disconnected.");
      //     break;
      //   }
      //
      //   var msg = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
      //   Console.WriteLine("=== RAW MESSAGE START ===");
      //   Console.WriteLine(msg);
      //   Console.WriteLine("=== RAW MESSAGE END ===");
      // }
      // var dbgIn = process.StandardInput.BaseStream;
      // var dbgOut = process.StandardOutput.BaseStream;

      var formatter = new JsonMessageFormatter
      {
        ProtocolVersion = new Version(1, 0)
      };

      var handler = new HeaderDelimitedMessageHandler(clientStream, clientStream, formatter);
      var clientRpc = new JsonRpc(handler);

      // var dbgRpc = new JsonRpc(dbgIn, dbgOut);

      clientRpc.AddLocalRpcTarget(new NvimDapHandler());
      clientRpc.TraceSource.Switch.Level = SourceLevels.All;
      clientRpc.TraceSource.Listeners.Add(new JsonRpcLogger(logger));
      // dbgRpc.AddLocalRpcTarget(new ProxyForwarder("dbg->client", clientRpc));

      clientRpc.StartListening();
      // dbgRpc.StartListening();

      Console.WriteLine("Proxying started. Press Ctrl+C to stop.");

      await process.WaitForExitAsync();
      Console.WriteLine("Debugger exited, closing client.");
    }
  }
}

public sealed record DapInitializeRequest(
    string AdapterID,
    string ClientID,
    string ClientName,
    bool ColumnsStartAt1,
    bool LinesStartAt1,
    string Locale,
    string PathFormat,
    bool SupportsProgressReporting,
    bool SupportsRunInTerminalRequest,
    bool SupportsStartDebuggingRequest,
    bool SupportsVariableType
);

public class DapProxyHandler(ILogger logger, DebugAdapterConnection debuggerConnection)
{
  public async Task OnClientRequest(object sender, RequestReceivedEventArgs e)
  {
    logger.LogInformation($"Client -> Debugger: {e.Request.Command}");

    // Handle specific commands if needed
    switch (e.Request.Command)
    {
      case "initialize":
        Console.WriteLine("RECEIVED INITIALIZE FROM CLIENT");
        // Could modify the request here if needed
        break;
      case "launch":
        logger.LogInformation("Launch request from client");
        break;
    }

    // Forward to debugger (1:1 proxy)
    await debuggerConnection.SendRequestAsync(e.Request);
  }

  public async Task OnDebuggerRequest(object sender, RequestReceivedEventArgs e)
  {
    logger.LogInformation($"Debugger -> Client: {e.Request.Command}");
    // Forward debugger requests back to client if needed
    // (This is rare, usually debugger sends responses/events)
  }

  public async Task OnDebuggerResponse(object sender, ResponseReceivedEventArgs e)
  {
    logger.LogInformation($"Debugger response: {e.Response.Command} (success: {e.Response.Success})");
    // Responses are automatically forwarded back to the original requester
  }

  public async Task OnDebuggerEvent(object sender, EventReceivedEventArgs e)
  {
    logger.LogInformation($"Debugger event: {e.Event.EventType}");
    // Events are automatically forwarded to the client
  }
}