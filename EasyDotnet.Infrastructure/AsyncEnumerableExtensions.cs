namespace EasyDotnet.Infrastructure;

public static class AsyncEnumerableExtensions
{
  public static async Task<List<T>> ToListAsync<T>(
      this IAsyncEnumerable<T> source,
      CancellationToken cancellationToken)
  {
    var list = new List<T>();
    await foreach (var item in source.WithCancellation(cancellationToken))
    {
      list.Add(item);
    }
    return list;
  }

  public static async Task<List<T>> ToListAsync<T>(
      this Task<IAsyncEnumerable<T>> sourceTask,
      CancellationToken cancellationToken)
  {
    var source = await sourceTask.ConfigureAwait(false);
    return await source.ToListAsync(cancellationToken).ConfigureAwait(false);
  }
}