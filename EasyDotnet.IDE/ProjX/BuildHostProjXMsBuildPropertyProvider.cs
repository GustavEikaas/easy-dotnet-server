using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.ProjXLanguageServer.Services;

namespace EasyDotnet.IDE.ProjX;

public sealed class BuildHostProjXMsBuildPropertyProvider(
    IBuildHostManager buildHostManager) : IProjXMsBuildPropertyProvider
{
  public async Task<IReadOnlyDictionary<string, string?>> GetPropertiesAsync(
      string projectPath,
      string[] propertyNames,
      CancellationToken cancellationToken)
  {
    var result = await GetFirstSuccessfulEvaluationAsync(projectPath, cancellationToken);
    var raw = result.Project!.Raw;
    var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    foreach (var propertyName in propertyNames)
    {
      properties[propertyName] = propertyName switch
      {
        "ManagePackageVersionsCentrally" => raw.ManagePackageVersionsCentrally ? "true" : "false",
        "DirectoryPackagesPropsPath" => raw.DirectoryPackagesPropsPath,
        "DirectoryBuildPropsPath" => raw.DirectoryBuildPropsPath,
        "DirectoryBuildTargetsPath" => raw.DirectoryBuildTargetsPath,
        _ => null
      };
    }

    return properties;
  }

  private async Task<ProjectEvaluationResult> GetFirstSuccessfulEvaluationAsync(
      string projectPath,
      CancellationToken cancellationToken)
  {
    ProjectEvaluationResult? firstFailure = null;

    await foreach (var result in buildHostManager.GetProjectPropertiesBatchAsync(
        new GetProjectPropertiesBatchRequest([projectPath], Configuration: null),
        cancellationToken))
    {
      if (result.Success && result.Project is not null)
      {
        return result;
      }

      firstFailure ??= result;
    }

    var error = firstFailure?.Error?.Message ?? $"MSBuild evaluation produced no result for {projectPath}.";
    throw new InvalidOperationException(error);
  }
}
