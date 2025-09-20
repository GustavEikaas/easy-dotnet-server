using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyDotnet.MTP.RPC.Models;
using Newtonsoft.Json;
using StreamJsonRpc;

namespace EasyDotnet.MTP;

internal class MtpServer
{
  private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TestNodeUpdate[]>> _listeners = new();
  private readonly ConcurrentDictionary<Guid, List<TestNodeUpdate>> _buffers = new();

  public void RegisterResponseListener(Guid runId, TaskCompletionSource<TestNodeUpdate[]> task) => _ = _listeners.TryAdd(runId, task);

  public void RemoveResponseListener(Guid runId) => _ = _listeners.TryRemove(runId, out _);

  [JsonRpcMethod("client/attachDebugger", UseSingleObjectParameterDeserialization = true)]
  public static Task AttachDebuggerAsync(AttachDebuggerInfo attachDebuggerInfo) => throw new NotImplementedException();

  [JsonRpcMethod("testing/testUpdates/tests")]
  public Task TestsUpdateAsync(Guid runId, TestNodeUpdate[]? changes)
  {
    _listeners.TryGetValue(runId, out var handler);
    if (handler is null)
    {
      return Task.CompletedTask;
    }

    if (changes is null)
    {
      var success = _buffers.TryGetValue(runId, out var result);
      if (!success || result is null)
      {
        throw new Exception("No result from server");
      }
      handler.SetResult([.. result]);
      _buffers.TryRemove(runId, out _);
      _listeners.TryRemove(runId, out _);
    }
    else
    {
      _buffers.GetOrAdd(runId, _ => []).AddRange(changes);
    }
    return Task.CompletedTask;
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