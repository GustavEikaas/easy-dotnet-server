using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services.NetCoreDbg;

public class NetcoreDbgService(
    MsBuildService msBuildService,
    ILogger<NetcoreDbgClient> netcoreDbgLogger,
    ILogger<TcpDapClient> tcpDapClientLogger,
    ILogger<NetcoreDbgService> logger)
{
  private NetcoreDbgClient? _netcoreDbgClient;
  private TcpDapClient? _tcpDapClient;
  private readonly Dictionary<int, int> _clientToDebuggerSeq = [];
  private int _seqCounter = 1;
  private static readonly JsonSerializerOptions JsonSerializerOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  public void Start()
  {
    _seqCounter = 1;
    _clientToDebuggerSeq.Clear();

    var tpcDapClient = InitializeTcpClient();
    _tcpDapClient = tpcDapClient;
    Task.Run(async () =>
    {
      InitializeNetcoreDbgClient();
      logger.LogInformation("Waiting for client...");

      await RunWithLogging(async () => await _tcpDapClient.StartAndConnect(() =>
      {
        Stop();
        return Task.CompletedTask;
      }));
    });

  }

  private void InitializeNetcoreDbgClient() => _netcoreDbgClient = new NetcoreDbgClient(
            logger: netcoreDbgLogger,
            callback: async (message) => await RunWithLogging(async () =>
            {

              var node = JsonNode.Parse(message) ?? throw new Exception("Messages cant be null");

              switch (node["type"]?.GetValue<string>())
              {
                case "response":
                  await HandleResponseMessage(node);
                  break;
                case "event":
                  await HandleEventMessage(node, MessageDirection.DebuggerToClient);
                  break;
                default:
                  await HandleOtherMessage(node, MessageDirection.DebuggerToClient);
                  break;
              }
            }),
            exitHandler: () =>
            {
              logger.LogInformation("netcoredbg exited, shutting down TCP server...");
              Stop();
            }
        );


  private TcpDapClient InitializeTcpClient()
  {
    var tcpClient = new TcpDapClient(tcpDapClientLogger, async (message) => await RunWithDapErrorHandling(async () =>
                                          {
                                            if (_netcoreDbgClient is null)
                                            {
                                              throw new Exception("NetcoreDbg client is null");
                                            }

                                            var node = JsonNode.Parse(message) ?? throw new Exception("Message cannot be null");

                                            switch (node["type"]?.GetValue<string>())
                                            {
                                              case "request":
                                                await HandleRequestMessage(node);
                                                break;
                                              case "event":
                                                await HandleEventMessage(node, MessageDirection.ClientToDebugger);
                                                break;
                                              default:
                                                await HandleOtherMessage(node, MessageDirection.ClientToDebugger);
                                                break;
                                            }
                                          }));
    return tcpClient;
  }

  private async Task HandleRequestMessage(JsonNode node)
  {
    var clientSeq = node["seq"]!.GetValue<int>();
    var newSeq = _seqCounter++;

    _clientToDebuggerSeq[newSeq] = clientSeq;
    node["seq"] = newSeq;

    logger.LogInformation("{Direction} {command}: {message}", DirectionLabel(MessageDirection.ClientToDebugger), node["command"], JsonSerializer.Serialize(node, JsonSerializerOptions));
    var maybeModified = await RefineAttachRequestAsync(node);
    var outbound = maybeModified ?? node.ToJsonString();

    await _netcoreDbgClient!.SendMessageAsync(outbound, CancellationToken.None);
  }

  private async Task HandleResponseMessage(JsonNode node)
  {
    var dbgReqSeq = node["request_seq"]?.GetValue<int>();
    if (dbgReqSeq is not null &&
        _clientToDebuggerSeq.TryGetValue(dbgReqSeq.Value, out var clientSeq))
    {
      node["request_seq"] = clientSeq;
    }

    node["seq"] = _seqCounter++;
    var inbound = node.ToJsonString();
    logger.LogInformation("{Direction} {command}: {message}", DirectionLabel(MessageDirection.DebuggerToClient), node["command"], JsonSerializer.Serialize(node, JsonSerializerOptions));
    await _tcpDapClient!.SendMessageAsync(inbound, CancellationToken.None);
  }

  private async Task HandleEventMessage(JsonNode node, MessageDirection direction)
  {
    node["seq"] = _seqCounter++;
    var serialized = node.ToJsonString();

    if (direction == MessageDirection.DebuggerToClient)
    {
      // logger.LogInformation("INBOUND event {event}: {serialized}", node["event"], serialized);
      await _tcpDapClient!.SendMessageAsync(serialized, CancellationToken.None);
    }
    else
    {
      // logger.LogInformation("OUTBOUND event {event}: {serialized}", node["event"], serialized);
      await _netcoreDbgClient!.SendMessageAsync(serialized, CancellationToken.None);
    }
  }

  private async Task HandleOtherMessage(JsonNode node, MessageDirection direction)
  {
    node["seq"] = _seqCounter++;
    var serialized = node.ToJsonString();

    if (direction == MessageDirection.DebuggerToClient)
    {
      logger.LogInformation("INBOUND other: {serialized}", serialized);
      await _tcpDapClient!.SendMessageAsync(serialized, CancellationToken.None);
    }
    else
    {
      logger.LogInformation("OUTBOUND other: {serialized}", serialized);
      await _netcoreDbgClient!.SendMessageAsync(serialized, CancellationToken.None);
    }
  }
  public void Stop()
  {
    try
    {
      _tcpDapClient?.Dispose();
    }
    catch (Exception ex) { logger.LogError(ex, "Error disposing TcpDapClient"); }

    try
    {
      _netcoreDbgClient?.Dispose();
    }
    catch (Exception ex) { logger.LogError(ex, "Error disposing NetcoreDbgClient"); }

    _tcpDapClient = null;
    _netcoreDbgClient = null;

    logger.LogInformation("TCP proxy stopped.");
  }

  private async Task RunWithLogging(Func<Task> taskFunc)
  {
    try
    {
      await taskFunc();
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "An error occurred");
    }
  }


  private async Task RunWithDapErrorHandling(Func<Task> taskFunc)
  {
    try
    {
      await taskFunc();
    }
    catch (DapException dex)
    {
      logger.LogError(dex, "DAP exception during message processing");

      if (_tcpDapClient is not null)
      {
        try
        {
          await _tcpDapClient.SendMessageAsync(dex.DapErrorMessage.ToSerializedResponse(), CancellationToken.None);
        }
        catch (Exception innerEx)
        {
          logger.LogError(innerEx, "Failed to send DAP exception response");
        }
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "An error occurred");
    }
  }

  private async Task<string?> RefineAttachRequestAsync(JsonNode node)
  {
    if (node["type"]?.GetValue<string>() != "request" ||
        node["command"]?.GetValue<string>() != "attach")
      return null;

    var args = node["arguments"];
    if (args?["request"]?.GetValue<string>() != "attach")
    {
      return null;
    }

    var seq = node["seq"]?.GetValue<int>() ?? throw new InvalidOperationException("Sequence number (seq) is missing in DAP message");


    if (args["launch_args"]?["project"]?.GetValue<string>() is not string projectPath)
    {
      _ = _clientToDebuggerSeq.TryGetValue(seq, out var clientSeq);

      throw new DapException(
          command: node["command"]!.GetValue<string>(),
          seq: seq,
          requestSeq: clientSeq,
          message: "Missing required launch arguments or project path"
      );
    }

    var launchArgs = args["launch_args"]!;

    var targetFrameworkMoniker = launchArgs["targetFramework"]?.GetValue<string>();
    var config = launchArgs["configuration"]?.GetValue<string>();
    var launchProfile = launchArgs["launchProfile"]?.GetValue<string>();

    var msBuildProject = await msBuildService.GetOrSetProjectPropertiesAsync(projectPath, targetFrameworkMoniker, config ?? "Debug");

    //TODO: resolve and pass launch profile to CreateInitRequestBasedOnProjectType
    var modifiedRequest = await InitializeRequestRewriter.CreateInitRequestBasedOnProjectType(projectPath, msBuildProject, Path.GetDirectoryName(projectPath)!, seq);

    logger.LogInformation("[REFINED] attach request converted:\n{stringifiedMessage}", modifiedRequest);

    return modifiedRequest;
  }

  private static string DirectionLabel(MessageDirection direction) =>
      direction == MessageDirection.ClientToDebugger ? "[REQUEST]: " : "[RESPONSE]: ";

  private enum MessageDirection
  {
    ClientToDebugger,
    DebuggerToClient
  }

}