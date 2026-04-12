namespace EasyDotnet.IDE.Picker;

public interface IPickerScopeRegistry
{
  void Register(IPickerScope scope);
  void Remove(Guid guid);
  IPickerScope? Get(Guid guid);
}