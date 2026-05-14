using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;

namespace EasyDotnet.IDE.BuildHost;

public static class BuildHostManagerExtensions
{
  public static Task<string?> ResolveSingleTfmAsync(
      this IBuildHostManager manager,
      string projectPath,
      string? configuration,
      CancellationToken ct) =>
      ResolveSingleTfmAsync(manager, projectPath, configuration, platform: null, ct);

  /// <summary>
  /// Returns the project's single TargetFramework, or null if the project
  /// is multi-TFM or could not be resolved. Used by build call sites to
  /// opt into FUTD on the BuildServer side.
  /// </summary>
  public static async Task<string?> ResolveSingleTfmAsync(
      this IBuildHostManager manager,
      string projectPath,
      string? configuration,
      string? platform,
      CancellationToken ct)
  {
    string? tfm = null;
    var request = new GetProjectPropertiesBatchRequest([projectPath], configuration, platform);
    await foreach (var r in manager.GetProjectPropertiesBatchAsync(request, ct))
    {
      if (!r.Success || r.TargetFramework is null) continue;
      if (tfm is null) tfm = r.TargetFramework;
      else return null;
    }
    return tfm;
  }
}