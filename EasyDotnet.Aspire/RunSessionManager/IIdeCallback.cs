using EasyDotnet.Aspire.Contracts;

namespace EasyDotnet.Aspire.RunSessionManager;

/// <summary>
/// The Aspire host's view of the editor side of the pipe. Implemented over the
/// named-pipe <see cref="StreamJsonRpc.JsonRpc"/> connection back to the IDE.
/// </summary>
public interface IIdeCallback
{
  /// <summary>
  /// Runs a resource as a managed process and completes when it exits, returning
  /// the exit code. The completion of this call is the session's exit signal.
  /// </summary>
  Task<int> RunManagedResourceAsync(RunManagedResourceRequest request, CancellationToken ct);

  /// <summary>Stops a previously started managed resource.</summary>
  Task StopManagedResourceAsync(string runId, CancellationToken ct);
}

/// <summary>
/// Sink for host -&gt; DCP run-session change notifications (JSON-lines over the
/// notify WebSocket). Implemented by the DCP server.
/// </summary>
public interface INotificationSink
{
  Task SendAsync(object notification, CancellationToken ct);
}