namespace EasyDotnet.IDE.Workspace.BuildConfiguration;

public interface IWorkspaceBuildConfigurationService
{
  event Action<WorkspaceBuildConfigurationChangedEventArgs>? ConfigurationChanged;

  Task<WorkspaceBuildConfiguration> GetActiveConfigurationAsync(CancellationToken cancellationToken = default);

  Task<IReadOnlyList<WorkspaceBuildConfiguration>> GetAvailableConfigurationsAsync(CancellationToken cancellationToken = default);

  Task<ResolvedBuildConfiguration> ResolveTargetAsync(string targetPath, CancellationToken cancellationToken = default);

  Task<IReadOnlyList<ResolvedBuildConfiguration>> ResolveTargetsAsync(
      IReadOnlyCollection<string> targetPaths,
      CancellationToken cancellationToken = default);

  Task SetActiveConfigurationAsync(WorkspaceBuildConfiguration configuration, CancellationToken cancellationToken = default);
}