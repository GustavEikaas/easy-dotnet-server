using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EasyDotnet.Services.NetCoreDbg;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;

namespace EasyDotnet.Services;

public class NetcoreDbgService(MsBuildService msBuildService, ILogger<NetcoreDbgService> logger)
{

  public Process StartProcess()
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

      var json = await ReadDapMessageAsync(input);
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

  private async Task<string?> ReadDapMessageAsync(Stream stream)
  {
    var headerBuilder = new StringBuilder();
    var buffer = new byte[1];

    // Read headers byte by byte until \r\n\r\n
    while (true)
    {
      var n = await stream.ReadAsync(buffer, 0, 1);
      if (n == 0) return null; // disconnected
      var c = (char)buffer[0];
      headerBuilder.Append(c);

      if (headerBuilder.Length >= 4 &&
          headerBuilder[^4] == '\r' &&
          headerBuilder[^3] == '\n' &&
          headerBuilder[^2] == '\r' &&
          headerBuilder[^1] == '\n')
        break;
    }

    // parse Content-Length
    var headers = headerBuilder.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    var contentLengthLine = headers.FirstOrDefault(h => h.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase));
    if (contentLengthLine == null) return null;
    var contentLength = int.Parse(contentLengthLine.Split(':')[1].Trim());

    // read exact bytes
    var messageBytes = new byte[contentLength];
    var read = 0;
    while (read < contentLength)
    {
      var n = await stream.ReadAsync(messageBytes, read, contentLength - read);
      if (n == 0) return null; // disconnected
      read += n;
    }

    // decode JSON
    return Encoding.UTF8.GetString(messageBytes);
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