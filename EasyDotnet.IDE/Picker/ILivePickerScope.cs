using EasyDotnet.IDE.Picker.Models;

namespace EasyDotnet.IDE.Picker;

public interface ILivePickerScope : IPickerScope
{
  Task<PickerChoice[]> QueryAsync(string query, CancellationToken ct);
}
