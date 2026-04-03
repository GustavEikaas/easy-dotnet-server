namespace EasyDotnet.IDE;

public static class AsyncEnumerableExtensions
{
  public static async Task<T?> FirstOrDefaultAsync<T>(
      this IAsyncEnumerable<T> source,
      CancellationToken cancellationToken = default)
  {
    await foreach (var item in source.WithCancellation(cancellationToken))
      return item;
    return default;
  }

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