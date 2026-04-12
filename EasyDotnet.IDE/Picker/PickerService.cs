using EasyDotnet.IDE.Picker.Models;

namespace EasyDotnet.IDE.Picker;

public sealed class PickerService : IPickerService
{
  public Task<T?> RequestPickerAsync<T>(
      string prompt,
      PickerChoice<T>[] choices,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default) => throw new NotImplementedException();

  public Task<T[]?> RequestMultiPickerAsync<T>(
      string prompt,
      PickerChoice<T>[] choices,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default) => throw new NotImplementedException();

  public Task<T?> RequestLivePickerAsync<T>(
      string prompt,
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default) => throw new NotImplementedException();

  public Task<T[]?> RequestMultiLivePickerAsync<T>(
      string prompt,
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default) => throw new NotImplementedException();
}