namespace EasyDotnet.BuildServer.MsBuildProject;

/// <summary>
/// Defines a typed MSBuild property with its name, documentation, and deserializer.
/// </summary>
public record MsBuildProperty<T>(
    string Name,
    string Description,
    Func<IReadOnlyDictionary<string, string?>, string, T> Deserialize,
    bool IsComputed = false
)
{
  public T GetValue(IReadOnlyDictionary<string, string?> values) => Deserialize(values, Name);
}