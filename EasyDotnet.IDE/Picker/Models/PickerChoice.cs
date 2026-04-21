namespace EasyDotnet.IDE.Picker.Models;

public sealed record PickerChoice(string Id, string Display);

public sealed record PickerChoice<T>(string Id, string Display, T Metadata)
{
  public PickerChoice ToWireType() => new(Id, Display);
}