using System.Collections.Concurrent;
using EasyDotnet.IDE.Picker.Models;

namespace EasyDotnet.IDE.Picker;

public class PickerScope<T> : IPickerScope
{
  protected readonly ConcurrentDictionary<string, T> _metadataDict;
  private readonly Func<T, CancellationToken, Task<PreviewResult>>? _previewFactory;
  private readonly IPickerScopeRegistry _registry;
  private readonly TaskCompletionSource<string[]?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private bool _disposed;

  public Guid Guid { get; } = Guid.NewGuid();
  public bool HasPreview => _previewFactory is not null;

  internal PickerScope(
      ConcurrentDictionary<string, T> metadataDict,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory,
      IPickerScopeRegistry registry)
  {
    _metadataDict = metadataDict;
    _previewFactory = previewFactory;
    _registry = registry;
  }

  public Task<PreviewResult?> GetPreviewAsync(string itemId, CancellationToken ct)
  {
    if (_previewFactory is null || !_metadataDict.TryGetValue(itemId, out var meta))
    {
      return Task.FromResult<PreviewResult?>(null);
    }

    return _previewFactory(meta, ct)!;
  }

  internal T? GetMetadata(string id) =>
      _metadataDict.TryGetValue(id, out var meta) ? meta : default;

  internal void Complete(string[]? selectedIds) => _tcs.TrySetResult(selectedIds);

  internal Task<string[]?> WaitAsync(CancellationToken ct) => _tcs.Task.WaitAsync(ct);

  public virtual void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _tcs.TrySetResult(null);
    _registry.Remove(Guid);
  }
}