using System.Collections.Concurrent;
using System.Threading.Channels;
using EasyDotnet.MTP.RPC.Models;
using Newtonsoft.Json;
using StreamJsonRpc;

namespace EasyDotnet.IDE.MTP;

internal class MtpServer
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

public enum LogLevel
{
  /// <summary>
  /// Trace.
  /// </summary>
  Trace = 0,

  /// <summary>
  /// Debug.
  /// </summary>
  Debug = 1,

  /// <summary>
  /// Information.
  /// </summary>
  Information = 2,

  /// <summary>
  /// Warning.
  /// </summary>
  Warning = 3,

  /// <summary>
  /// Error.
  /// </summary>
  Error = 4,

  /// <summary>
  /// Critical.
  /// </summary>
  Critical = 5,

  /// <summary>
  /// None.
  /// </summary>
  None = 6,
}