using System.Collections.Generic;
using StreamJsonRpc;

namespace EasyDotnet.IDE;

public static class JsonRpcExtensions
{
  public static IAsyncEnumerable<T> ToBatchedAsyncEnumerable<T>(
          this IEnumerable<T> source,
          int minBatchSize) => source.AsAsyncEnumerable(new JsonRpcEnumerableSettings
          {
            MinBatchSize = minBatchSize
          });
}
