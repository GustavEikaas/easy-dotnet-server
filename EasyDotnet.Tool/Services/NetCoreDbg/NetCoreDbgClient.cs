using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services.NetCoreDbg;

public class NetcoreDbgClient : IDisposable
{
  private readonly ILogger<NetcoreDbgClient> _logger;
  private readonly Process _process;
  private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingRequests = new();
  private int _seqCounter = 0;

  private readonly Action<string>? _globalMessageHandler;

  public bool IsRunning => !_process.HasExited;

  public NetcoreDbgClient(ILogger<NetcoreDbgClient> logger, Action<string> callback, Action? exitHandler = null)
  {
    _logger = logger;
    _process = StartProcess();
    _globalMessageHandler = callback;
    _process.Exited += (_, _) => exitHandler?.Invoke();
    StartReadingLoop(CancellationToken.None);
  }

  private static Process StartProcess()
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
      },
      EnableRaisingEvents = true,
    };

    process.Start();
    return process;
  }

  /// <summary>
  /// Sends a raw message to netcoredbg
  /// </summary>
  public async Task SendMessageAsync(string json, CancellationToken? cancellationToken = null) => await WriteDapMessageAsync(json, cancellationToken ?? CancellationToken.None);

  /// <summary>
  /// Sends a request and waits for its response
  /// </summary>
  public async Task<string> SendRequestAsync(JsonObject request, CancellationToken? cancellationToken = null)
  {
    var seq = ++_seqCounter;
    request["seq"] = seq;
    request["type"] = "request";

    var tcs = new TaskCompletionSource<string>();
    _pendingRequests[seq] = tcs;

    var message = request.ToJsonString();
    await WriteDapMessageAsync(message, cancellationToken ?? CancellationToken.None);

    return await tcs.Task;
  }

  private async Task WriteDapMessageAsync(string json, CancellationToken cancellationToken)
  {
    var bytes = Encoding.UTF8.GetBytes(json);
    var header = Encoding.UTF8.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");

    await _process.StandardInput.BaseStream.WriteAsync(header, cancellationToken);
    await _process.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken);
    await _process.StandardInput.BaseStream.FlushAsync(cancellationToken);

    _logger.LogDebug("Sent message: {Json}", json);
  }

  private void StartReadingLoop(CancellationToken? cancellationToken) => Task.Run(async () =>
                                      {
                                        try
                                        {
                                          var stdout = _process.StandardOutput.BaseStream;
                                          while (!_process.HasExited)
                                          {
                                            var json = await DapMessageReader.ReadDapMessageAsync(stdout, cancellationToken ?? CancellationToken.None);
                                            if (json == null) break;

                                            try
                                            {
                                              var node = JsonNode.Parse(json);
                                              if (node?["type"]?.GetValue<string>() == "response" &&
                                                      node["request_seq"]?.GetValue<int>() is int requestSeq &&
                                                      _pendingRequests.TryRemove(requestSeq, out var tcs))
                                              {
                                                tcs.SetResult(json);
                                              }
                                              else
                                              {
                                                _globalMessageHandler?.Invoke(json);
                                              }
                                            }
                                            catch (Exception ex)
                                            {
                                              _logger.LogError(ex, "Error parsing message: {Json}", json);
                                              _globalMessageHandler?.Invoke(json);
                                            }
                                          }
                                        }
                                        catch (Exception ex)
                                        {
                                          _logger.LogError(ex, "Error in reading loop");
                                        }
                                      });

  public void Dispose()
  {
    if (_process != null && !_process.HasExited)
    {
      _process.Kill();
      _process.Dispose();
    }
  }
}