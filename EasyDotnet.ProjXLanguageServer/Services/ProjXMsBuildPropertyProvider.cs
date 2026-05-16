namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IProjXMsBuildPropertyProvider
{
  Task<IReadOnlyDictionary<string, string?>> GetPropertiesAsync(
      string projectPath,
      string[] propertyNames,
      CancellationToken cancellationToken);
}

public sealed class UnavailableProjXMsBuildPropertyProvider : IProjXMsBuildPropertyProvider
{
  public Task<IReadOnlyDictionary<string, string?>> GetPropertiesAsync(
      string projectPath,
      string[] propertyNames,
      CancellationToken cancellationToken) =>
      throw new InvalidOperationException("ProjX MSBuild property evaluation is not configured.");
}
