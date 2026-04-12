namespace EasyDotnet.IDE.Picker.Models;

public sealed record PickerRequest(Guid Guid, string Prompt, PickerChoice[] Choices, bool Multi, bool Preview);