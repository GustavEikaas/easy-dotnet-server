using System.Collections.Concurrent;
using System.Threading.Channels;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Adapters.MTP;

public class MtpServer()
{
  private readonly ConcurrentDictionary<Guid, ChannelWriter<TestNodeUpdate>> _streamListeners = new();
  public void RegisterStreamListener(Guid runId, ChannelWriter<TestNodeUpdate> writer) => _streamListeners.TryAdd(runId, writer);
  public void RemoveStreamListener(Guid runId) => _streamListeners.TryRemove(runId, out _);

  [JsonRpcMethod("client/attachDebugger", UseSingleObjectParameterDeserialization = true)]
  public static Task AttachDebuggerAsync(AttachDebuggerInfo attachDebuggerInfo) => throw new NotImplementedException();

  [JsonRpcMethod("testing/testUpdates/tests")]
  public void TestsUpdate(Guid runId, TestNodeUpdate[]? changes)
  {
    if (_streamListeners.TryGetValue(runId, out var streamWriter))
    {
      if (changes is null)
      {
        streamWriter.TryComplete();
        _streamListeners.TryRemove(runId, out _);
      }
      else
      {
        foreach (var change in changes)
        {
          streamWriter.TryWrite(change);
        }
      }
    }
  }

  [JsonRpcMethod("telemetry/update", UseSingleObjectParameterDeserialization = true)]
  public Task TelemetryAsync(TelemetryPayload telemetry) => Task.CompletedTask;

  [JsonRpcMethod("client/log")]
  public Task LogAsync(LogLevel level, string message) => Task.CompletedTask;
}

public sealed record AttachDebuggerInfo([property: JsonProperty("processId")] int ProcessId);

public record TelemetryPayload([property: JsonProperty(nameof(TelemetryPayload.EventName))] string EventName, [property: JsonProperty("metrics")] IDictionary<string, string> Metrics);