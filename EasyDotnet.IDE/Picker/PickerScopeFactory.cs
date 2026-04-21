using EasyDotnet.IDE.Picker.Models;

namespace EasyDotnet.IDE.Picker;

public sealed class PickerScopeFactory(IPickerScopeRegistry registry)
{
  public PickerScope<T> CreatePicker<T>(
      PickerChoice<T>[] choices,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory)
  {
    var metadataDict = new System.Collections.Concurrent.ConcurrentDictionary<string, T>(
        choices.ToDictionary(c => c.Id, c => c.Metadata));
    var scope = new PickerScope<T>(metadataDict, previewFactory, registry);
    registry.Register(scope);
    return scope;
  }

  public LivePickerScope<T> CreateLivePicker<T>(
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory)
  {
    var scope = new LivePickerScope<T>(queryFactory, previewFactory, registry);
    registry.Register(scope);
    return scope;
  }
}