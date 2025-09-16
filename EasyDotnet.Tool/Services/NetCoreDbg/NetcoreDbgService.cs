using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services.NetCoreDbg;

public class NetcoreDbgService(MsBuildService msBuildService, ILogger<NetcoreDbgService> logger)
{
  private TcpListener? _listener;
  private Process? _process;

  public static Process StartProcess()
  {
    var process = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = "netcoredbg",
        Arguments = "--interpreter=vscode",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      }
    };
    process.Start();
    process.EnableRaisingEvents = true;
    return process;
  }


  public void Start()
  {
    if (_listener != null)
    {
      throw new InvalidOperationException("TCP server already running.");
    }

    _listener = new TcpListener(IPAddress.Any, 8086);
    _process = StartProcess();

    _process.EnableRaisingEvents = true;
    _process.Exited += (s, e) =>
    {
      logger.LogInformation("netcoredbg exited, shutting down TCP server...");
      Stop();
    };

    _listener.Start();

    Task.Run(async () =>
    {
      logger.LogInformation("Waiting for client...");
      var client = await _listener.AcceptTcpClientAsync();
      logger.LogInformation("Client connected: {EndPoint}", client.Client.RemoteEndPoint);

      await HandleClientAsync(client, _process);
    });

  }
  public void Stop()
  {
    _listener?.Stop();
    _listener = null;

    if (_process != null && !_process.HasExited)
    {
      _process.Kill();
      _process.Dispose();
    }

    _process = null;
  }

  private async Task HandleClientAsync(TcpClient client, Process process)
  {
    process.Exited += (sender, args) => client.Dispose();
    using (client)
    {
      var clientStream = client.GetStream();
      var dbgIn = process.StandardInput.BaseStream;
      var dbgOut = process.StandardOutput.BaseStream;

      // Start both proxy loops
      var clientToDbg = Task.Run(() => RunWithLogging(() => ProxyLoop(clientStream, dbgIn, RefineIncomingAsync), "client->debugger"));
      var dbgToClient = Task.Run(() => RunWithLogging(() => ProxyLoop(dbgOut, clientStream, RefineOutboundAsync), "debugger->client"));

      Console.WriteLine("Proxying started. Press Ctrl+C to stop.");

      await Task.WhenAny(clientToDbg, dbgToClient, process.WaitForExitAsync());

      Console.WriteLine("Shutting down proxy.");
    }
  }

  private async Task ProxyLoop(Stream input, Stream output, Func<string, Task<string>> refine)
  {

    while (true)
    {

      var json = await DapMessageReader.ReadDapMessageAsync(input, CancellationToken.None);
      if (json == null) break; // Stream closed
      json = await refine(json);

      // 3. Encode the refined JSON and frame it
      var jsonBytes = Encoding.UTF8.GetBytes(json);
      var headerBytes = Encoding.UTF8.GetBytes($"Content-Length: {jsonBytes.Length}\r\n\r\n");

      // 4. Send header + body
      await output.WriteAsync(headerBytes, 0, headerBytes.Length);
      await output.WriteAsync(jsonBytes, 0, jsonBytes.Length);




      // json = await refine(json);
      //
      // Console.WriteLine($"Refine incoming {json}");
      // var message = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";
      // Console.WriteLine($"Server->NetcoreDbg: {message}");
      // await writer.WriteAsync(message);
    }
  }
  private async Task RunWithLogging(Func<Task> taskFunc, string direction)
  {
    try
    {
      await taskFunc();
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error in proxy loop {Direction}", direction);
    }
    finally
    {
      logger.LogInformation("Proxy loop {Direction} ended", direction);
    }
  }


  /// Incoming: client → debugger
  private async Task<string> RefineIncomingAsync(string json)
  {
    Console.WriteLine($"[INCOMING] {json}");

    try
    {
      if (json.Contains("\"command\":\"attach\"", StringComparison.OrdinalIgnoreCase) &&
          json.Contains("\"type\":\"request\"", StringComparison.OrdinalIgnoreCase))
      {
        var node = JsonNode.Parse(json);
        if (node == null) return json;

        var modified = await RefineAttachRequestAsync(node);
        return modified ?? json;
      }

      return json;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"[REFINE ERROR] {ex.Message}");
      return json;
    }
  }
  private async Task<string?> RefineAttachRequestAsync(JsonNode node)
  {
    if (node["type"]?.GetValue<string>() != "request" ||
        node["command"]?.GetValue<string>() != "attach")
      return null;

    var args = node["arguments"];
    if (args?["request"]?.GetValue<string>() != "attach")
      return null;

    var projectPath = args["project"]?.GetValue<string>()
                      ?? throw new InvalidOperationException("project path missing");

    var msBuildProject = await msBuildService.GetOrSetProjectPropertiesAsync(projectPath);


    var seq = node["seq"]?.GetValue<int>() ?? throw new InvalidOperationException("Sequence number (seq) is missing in DAP message");
    var modifiedRequest = await InitializeRequestRewriter.CreateInitRequestBasedOnProjectType(projectPath, msBuildProject, Path.GetDirectoryName(projectPath)!, seq);

    var stringifiedMessage = modifiedRequest.ToJsonString();

    logger.LogInformation("[REFINED] attach request converted:\n{stringifiedMessage}", stringifiedMessage);

    return stringifiedMessage;
  }


  /// Outbound: debugger → client
  private async Task<string> RefineOutboundAsync(string json)
  {
    // TODO: add filtering/modification logic
    Console.WriteLine($"[OUTBOUND] {json}");
    return await Task.FromResult(json);
  }
}