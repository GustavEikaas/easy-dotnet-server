using EasyDotnet.IDE.Picker.Models;

namespace EasyDotnet.IDE.Picker;

public interface IPickerScope : IDisposable
{
  Guid Guid { get; }

  bool HasPreview { get; }

  Task<PreviewResult?> GetPreviewAsync(string itemId, CancellationToken ct);
}