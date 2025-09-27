using EasyDotnet.Domain.Models.MsBuild.Build;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Domain.Models.MsBuild.SDK;

namespace EasyDotnet.Application.Interfaces;

public interface IMsBuildService
{
  /// <summary>
  /// Retrieves the base installation path for the .NET runtime (dotnet root).
  /// This path is typically two directory levels above the MSBuild path, such as 
  /// "C:\Program Files\dotnet" on Windows or "/usr/share/dotnet" on Unix systems.
  /// </summary>
  /// <returns>The root path of the dotnet installation.</returns>
  string GetDotnetSdkBasePath();
  SdkInstallation[] QuerySdkInstallations();
  Task<bool> AddProjectReferenceAsync(string projectPath, string targetPath, CancellationToken cancellationToken = default);
  Task<DotnetProject> GetOrSetProjectPropertiesAsync(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug", CancellationToken cancellationToken = default);
  Task<DotnetProject> GetProjectPropertiesAsync(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug", CancellationToken cancellationToken = default);
  Task<List<string>> GetProjectReferencesAsync(string projectPath, CancellationToken cancellationToken = default);
  Task InvalidateProjectProperties(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug");
  Task<bool> RemoveProjectReferenceAsync(string projectPath, string targetPath, CancellationToken cancellationToken = default);
  Task<BuildResult> RequestBuildAsync(string targetPath, string? targetFrameworkMoniker, string? buildArgs, string configuration = "Debug", CancellationToken cancellationToken = default);
}