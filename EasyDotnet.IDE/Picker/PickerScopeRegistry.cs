using System.Collections.Concurrent;

namespace EasyDotnet.IDE.Picker;

public sealed class PickerScopeRegistry : IPickerScopeRegistry
{
  private readonly ConcurrentDictionary<Guid, IPickerScope> _scopes = new();

  public void Register(IPickerScope scope) => _scopes[scope.Guid] = scope;
  public void Remove(Guid guid) => _scopes.TryRemove(guid, out _);
  public IPickerScope? Get(Guid guid) => _scopes.TryGetValue(guid, out var scope) ? scope : null;
}