namespace EasyDotnet.MsBuild;

public sealed class MsBuildPropertyBag(IReadOnlyDictionary<string, string?> values)
{
  private readonly IReadOnlyDictionary<string, string?> _values = values ?? throw new ArgumentNullException(nameof(values));

  /// <summary>
  /// Get a known property definition and deserialize it.
  /// </summary>
  public T Get<T>(MsBuildProperty<T> property) => property.GetValue(_values);

  /// <summary>
  /// Dynamically get any arbitrary MSBuild property by name.
  /// </summary>
  public string? Get(string name)
      => MsBuildValueParsers.AsString(_values, name);

  /// <summary>
  /// Try get a typed bool dynamically by name.
  /// </summary>
  public bool GetBool(string name)
      => MsBuildValueParsers.AsBool(_values, name);
}
