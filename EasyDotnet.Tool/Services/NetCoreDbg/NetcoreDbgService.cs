using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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

  public void Start()
  {
    _tcpDapClient = new TcpDapClient(tcpDapClientLogger, async (message) => await RunWithLogging(async () =>
      {

        if (_netcoreDbgClient is null)
        {
          throw new Exception("NetcoreDbg client is null");
        }

        var node = JsonNode.Parse(message) ?? throw new Exception("Message cannot be null");

        switch (node["type"]?.GetValue<string>())
        {
          case "request":
            {
              var clientSeq = node["seq"]!.GetValue<int>();
              var newSeq = _seqCounter++;

              _clientToDebuggerSeq[newSeq] = clientSeq;
              node["seq"] = newSeq;

              var maybeModified = await RefineAttachRequestAsync(node);
              var outbound = maybeModified ?? node.ToJsonString();

              logger.LogInformation("OUTBOUND request {command}: {outbound}", node["command"], outbound);
              await _netcoreDbgClient.SendMessageAsync(outbound, CancellationToken.None);
              break;
            }

          case "event":
            {
              node["seq"] = _seqCounter++;
              var outbound = node.ToJsonString();
              logger.LogInformation("OUTBOUND event {event}: {outbound}", node["event"], outbound);
              await _netcoreDbgClient.SendMessageAsync(outbound, CancellationToken.None);
              break;
            }

          default: // should not usually happen client→debugger
            {
              node["seq"] = _seqCounter++;
              var outbound = node.ToJsonString();
              logger.LogInformation("OUTBOUND other: {outbound}", outbound);
              await _netcoreDbgClient.SendMessageAsync(outbound, CancellationToken.None);
              break;
            }
        }
      }));

    Task.Run(async () =>
    {

      _netcoreDbgClient = new NetcoreDbgClient(
          logger: netcoreDbgLogger,
          callback: async (message) => await RunWithLogging(async () =>
          {

            var node = JsonNode.Parse(message) ?? throw new Exception("Messages cant be null");

            switch (node["type"]?.GetValue<string>())
            {
              case "response":
                {
                  var dbgReqSeq = node["request_seq"]?.GetValue<int>();
                  if (dbgReqSeq is not null &&
                      _clientToDebuggerSeq.TryGetValue(dbgReqSeq.Value, out var clientSeq))
                  {
                    node["request_seq"] = clientSeq;
                  }

                  node["seq"] = _seqCounter++;
                  var inbound = node.ToJsonString();
                  logger.LogInformation("INBOUND response {command}: {inbound}", node["command"], inbound);
                  await _tcpDapClient.SendMessageAsync(inbound, CancellationToken.None);
                  break;
                }

              case "event":
                {
                  node["seq"] = _seqCounter++;
                  var inbound = node.ToJsonString();
                  logger.LogInformation("INBOUND event {event}: {inbound}", node["event"], inbound);
                  await _tcpDapClient.SendMessageAsync(inbound, CancellationToken.None);
                  break;
                }

              default:
                {
                  node["seq"] = _seqCounter++;
                  var inbound = node.ToJsonString();
                  logger.LogInformation("INBOUND other: {inbound}", inbound);
                  await _tcpDapClient.SendMessageAsync(inbound, CancellationToken.None);
                  break;
                }
            }
          }),
          exitHandler: () =>
          {
            logger.LogInformation("netcoredbg exited, shutting down TCP server...");
            Stop();
          }
      );

      logger.LogInformation("Waiting for client...");
      await RunWithLogging(async () => await _tcpDapClient.StartAndConnect(() =>
      {
        Stop();
        return Task.CompletedTask;
      }));
    });

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

}