using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
                                          var reader = _process.StandardOutput.BaseStream;
                                          while (!_process.HasExited)
                                          {
                                            var json = await ReadDapMessageAsync(reader, cancellationToken ?? CancellationToken.None);
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

  private static async Task<string?> ReadDapMessageAsync(Stream stream, CancellationToken cancellationToken)
  {
    var headerBuilder = new StringBuilder();
    var buffer = new byte[1];

    while (true)
    {
      var n = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
      if (n == 0) return null;
      var c = (char)buffer[0];
      headerBuilder.Append(c);

      if (headerBuilder.Length >= 4 &&
          headerBuilder[^4] == '\r' &&
          headerBuilder[^3] == '\n' &&
          headerBuilder[^2] == '\r' &&
          headerBuilder[^1] == '\n')
        break;
    }

    var headers = headerBuilder.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    var contentLengthLine = Array.Find(headers, h => h.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase));
    if (contentLengthLine == null) return null;

    var contentLength = int.Parse(contentLengthLine.Split(':')[1].Trim());
    var messageBytes = new byte[contentLength];
    var read = 0;

    while (read < contentLength)
    {
      var n = await stream.ReadAsync(messageBytes.AsMemory(read, contentLength - read), cancellationToken);
      if (n == 0) return null;
      read += n;
    }

    return Encoding.UTF8.GetString(messageBytes);
  }

  public void Dispose()
  {
    if (_process != null && !_process.HasExited)
    {
      _process.Kill();
      _process.Dispose();
    }
  }
}