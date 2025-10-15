using EasyDotnet.Application.Interfaces;

namespace EasyDotnet.Infrastructure.Aspire;

public class AspireService(IMsBuildService msBuildService) : IAspireService
{
  public async Task<string[]> GetExecutableIntegrations(string appHost, CancellationToken cancellationToken)
  {
    var appHostProject = await msBuildService.GetOrSetProjectPropertiesAsync(appHost, cancellationToken: cancellationToken);
    if (!appHostProject.IsAspireHost)
    {
      throw new Exception("Apphost is not an Aspire host project");
    }
    var references = await msBuildService.GetProjectReferencesAsync(appHost, cancellationToken);
    if (references is null || references.Count == 0)
    {
      throw new Exception("Apphost has no project references");
    }

    var executablePaths = await Task.WhenAll(
                references.Select(async projectPath =>
                {
                  var project = await msBuildService.GetOrSetProjectPropertiesAsync(projectPath, cancellationToken: cancellationToken);

                  return project.OutputType?.Equals("Exe", StringComparison.OrdinalIgnoreCase) == true
                     || project.OutputType?.Equals("WinExe", StringComparison.OrdinalIgnoreCase) == true
                  ? projectPath
                  : null;
                })
            );

    return executablePaths.Where(path => path != null)!.ToArray()!;
  }
}