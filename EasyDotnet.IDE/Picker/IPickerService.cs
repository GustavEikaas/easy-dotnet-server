using EasyDotnet.IDE.Picker.Models;

namespace EasyDotnet.IDE.Picker;

public interface IPickerService
{
  Task<T?> RequestPickerAsync<T>(
      string prompt,
      PickerChoice<T>[] choices,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default);

  Task<T[]?> RequestMultiPickerAsync<T>(
      string prompt,
      PickerChoice<T>[] choices,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default);

  Task<T?> RequestLivePickerAsync<T>(
      string prompt,
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default);

  Task<T[]?> RequestMultiLivePickerAsync<T>(
      string prompt,
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default);
}