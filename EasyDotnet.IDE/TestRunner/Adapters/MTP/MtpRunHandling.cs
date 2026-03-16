using System.Threading.Channels;
using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Models;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Adapters.MTP;

internal static class MtpRunHandling
{
  internal static bool IsDisconnectException(Exception ex)
  {
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

        if (!seen.Add(result.NativeId)) continue;

        await onResult(result);
      }
    }
    catch (Exception ex) when (IsDisconnectException(ex))
    {
      if (seen.Count >= expectedResultCount) return;
      throw new OperationCanceledException("MTP disconnected during run", ex);
    }
    if (abortRequested() && seen.Count < expectedResultCount)
      throw new OperationCanceledException("Debug session ended");
  }
}