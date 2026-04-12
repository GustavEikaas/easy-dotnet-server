namespace EasyDotnet.IDE.Picker.Models;

public sealed record LivePickerRequest(Guid Guid, string Prompt, bool Multi, bool Preview);