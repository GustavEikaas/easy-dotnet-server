using System.Threading.Channels;
using EasyDotnet.IDE.TestRunner.Adapters;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Models;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Adapters.MTP;

internal static class MtpRunHandling
{
  internal static bool IsDisconnectException(Exception ex)
  {
    // ChannelClosedException is commonly thrown when the writer completes with an error.
    if (ex is ChannelClosedException { InnerException: not null } cc)
      ex = cc.InnerException!;

    return ex is ConnectionLostException or ObjectDisposedException;
  }

  internal static async Task ForwardResultsOrCancelAsync(
      IAsyncEnumerable<TestNodeUpdate> updates,
      Func<TestRunResult, Task> onResult,
      ILogger logger,
      Func<bool> abortRequested,
      int expectedResultCount,
      CancellationToken ct)
  {
    var seen = new HashSet<string>(StringComparer.Ordinal);

    try
    {
      await foreach (var update in updates.WithCancellation(ct))
      {
        TestRunResult? result;
        try { result = update.ToTestRunResult(); }
        catch (ArgumentOutOfRangeException ex)
        {
          logger.LogError(ex, "Skipping result with unmapped MTP execution state for {Uid}", update.Node.Uid);
          continue;
        }

        if (result is null) continue;

        // Be defensive against duplicate terminal updates.
        if (!seen.Add(result.NativeId)) continue;

        await onResult(result);
      }
    }
    catch (Exception ex) when (IsDisconnectException(ex))
    {
      // If we already got everything we expected, don't retroactively turn a late disconnect into a cancel.
      if (seen.Count >= expectedResultCount) return;

      // Treat abrupt disconnect as cancellation when running under debug.
      throw new OperationCanceledException("MTP disconnected during run", ex);
    }

    // If the debug session ended early, treat the operation as cancelled so the service can:
    // - mark in-flight nodes as Cancelled
    // - keep already reported results
    if (abortRequested() && seen.Count < expectedResultCount)
      throw new OperationCanceledException("Debug session ended");
  }
}
