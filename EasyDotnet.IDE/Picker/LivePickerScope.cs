using System.Collections.Concurrent;
using EasyDotnet.IDE.Picker.Models;

namespace EasyDotnet.IDE.Picker;

public sealed class LivePickerScope<T> : PickerScope<T>, ILivePickerScope
{
  private readonly Func<string, CancellationToken, Task<PickerChoice<T>[]>> _queryFactory;
  private readonly ConcurrentDictionary<string, PickerChoice<T>[]> _queryCache = new();
  private readonly object _ctsLock = new();
  private CancellationTokenSource? _inFlightCts;

  internal LivePickerScope(
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory,
      IPickerScopeRegistry registry)
      : base(new ConcurrentDictionary<string, T>(), previewFactory, registry) => _queryFactory = queryFactory;

  public async Task<PickerChoice[]> QueryAsync(string query, CancellationToken ct)
  {
    if (_queryCache.TryGetValue(query, out var cached))
    {
      return cached.Select(c => c.ToWireType()).ToArray();
    }

    CancellationTokenSource myCts;
    lock (_ctsLock)
    {
      _inFlightCts?.Cancel();
      myCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      _inFlightCts = myCts;
    }

    try
    {
      var results = await _queryFactory(query, myCts.Token);

      _queryCache[query] = results;
      foreach (var choice in results)
      {
        _metadataDict[choice.Id] = choice.Metadata;
      }

      return results.Select(c => c.ToWireType()).ToArray();
    }
    catch (OperationCanceledException)
    {
      return [];
    }
    finally
    {
      lock (_ctsLock)
      {
        if (_inFlightCts == myCts)
        {
          _inFlightCts = null;
          myCts.Dispose();
        }
      }
    }
  }

  public override void Dispose()
  {
    lock (_ctsLock)
    {
      _inFlightCts?.Cancel();
      _inFlightCts?.Dispose();
      _inFlightCts = null;
    }
    base.Dispose();
  }
}