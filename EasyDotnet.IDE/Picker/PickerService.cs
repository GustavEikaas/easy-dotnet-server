using EasyDotnet.IDE.Picker.Models;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Picker;

public sealed class PickerService(
    PickerScopeFactory scopeFactory,
    JsonRpc jsonRpc,
    ILogger<PickerService> logger) : IPickerService
{
  private async Task<T[]?> RequestCoreAsync<T>(
      string prompt,
      PickerChoice<T>[] choices,
      bool multi,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory,
      CancellationToken ct)
  {
    var numbered = choices
        .Select((c, i) => c with { Display = $"{i + 1}. {c.Display}" })
        .ToArray();

    using var scope = scopeFactory.CreatePicker(numbered, previewFactory);

    var wireChoices = numbered.Select(c => c.ToWireType()).ToArray();
    var request = new PickerRequest(scope.Guid, prompt, wireChoices, multi, previewFactory is not null);

    var result = await jsonRpc.InvokeWithParameterObjectAsync<PickerResult?>("picker/pick", request, ct);

    if (result?.SelectedIds is null)
    {
      logger.LogDebug("User cancelled picker");
      return null;
    }

    return result.SelectedIds
        .Select(id => scope.GetMetadata(id))
        .OfType<T>()
        .ToArray();
  }

  public async Task<T?> RequestPickerAsync<T>(
      string prompt,
      PickerChoice<T>[] choices,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default)
  {
    var results = await RequestCoreAsync(prompt, choices, false, previewFactory, ct);
    return results is { Length: > 0 } ? results[0] : default;
  }

  public async Task<T[]?> RequestMultiPickerAsync<T>(
      string prompt,
      PickerChoice<T>[] choices,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default) =>
      await RequestCoreAsync(prompt, choices, true, previewFactory, ct);

  public async Task<T?> RequestLivePickerAsync<T>(
      string prompt,
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default)
  {
    var results = await RequestLiveCoreAsync(prompt, queryFactory, false, previewFactory, ct);
    return results is { Length: > 0 } ? results[0] : default;
  }

  public Task<T[]?> RequestMultiLivePickerAsync<T>(
      string prompt,
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory = null,
      CancellationToken ct = default) =>
      RequestLiveCoreAsync(prompt, queryFactory, true, previewFactory, ct);

  private async Task<T[]?> RequestLiveCoreAsync<T>(
      string prompt,
      Func<string, CancellationToken, Task<PickerChoice<T>[]>> queryFactory,
      bool multi,
      Func<T, CancellationToken, Task<PreviewResult>>? previewFactory,
      CancellationToken ct)
  {
    using var scope = scopeFactory.CreateLivePicker(queryFactory, previewFactory);

    var request = new LivePickerRequest(scope.Guid, prompt, multi, previewFactory is not null);
    var result = await jsonRpc.InvokeWithParameterObjectAsync<PickerResult?>("picker/live", request, ct);

    if (result?.SelectedIds is null)
    {
      logger.LogDebug("User cancelled live picker");
      return null;
    }

    return result.SelectedIds
        .Select(id => scope.GetMetadata(id))
        .OfType<T>()
        .ToArray();
  }
}