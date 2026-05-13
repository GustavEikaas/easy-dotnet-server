using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Settings;
using EasyDotnet.MsBuild;
using Microsoft.VisualStudio.SolutionPersistence.Model;

namespace EasyDotnet.IDE.Workspace.BuildConfiguration;

public sealed class WorkspaceBuildConfigurationService(
    IClientService clientService,
    ISolutionService solutionService,
    SettingsService settingsService) : IWorkspaceBuildConfigurationService
{
  private const string DebugBuildType = "Debug";
  private const string ReleaseBuildType = "Release";
  private const string DefaultPlatform = "Any CPU";

  public event Action<WorkspaceBuildConfigurationChangedEventArgs>? ConfigurationChanged;

  public async Task<WorkspaceBuildConfiguration> GetActiveConfigurationAsync(CancellationToken cancellationToken = default)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;
    if (solutionFile is null)
    {
      return FolderDefault();
    }

    var context = await GetSolutionContextAsync(solutionFile, cancellationToken);
    return context.Active;
  }

  public async Task<IReadOnlyList<WorkspaceBuildConfiguration>> GetAvailableConfigurationsAsync(CancellationToken cancellationToken = default)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;
    if (solutionFile is null)
    {
      return [FolderDefault()];
    }

    var context = await GetSolutionContextAsync(solutionFile, cancellationToken);
    return context.Available;
  }

  public async Task<ResolvedBuildConfiguration> ResolveTargetAsync(string targetPath, CancellationToken cancellationToken = default)
  {
    var resolved = await ResolveTargetsAsync([targetPath], cancellationToken);
    return resolved[0];
  }

  public async Task<IReadOnlyList<ResolvedBuildConfiguration>> ResolveTargetsAsync(
      IReadOnlyCollection<string> targetPaths,
      CancellationToken cancellationToken = default)
  {
    if (targetPaths.Count == 0)
    {
      return [];
    }

    var solutionFile = clientService.ProjectInfo?.SolutionFile;
    if (solutionFile is null)
    {
      var folderDefault = FolderDefault();
      return [.. targetPaths.Select(path => new ResolvedBuildConfiguration(
          path,
          folderDefault,
          folderDefault.BuildType,
          folderDefault.Platform,
          Build: true,
          Deploy: false,
          UsedProjectMapping: false))];
    }

    var context = await GetSolutionContextAsync(solutionFile, cancellationToken);
    var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionFile))
        ?? throw new InvalidOperationException("Solution directory could not be determined.");

    var projectsByPath = context.Model.SolutionProjects.ToDictionary(
        project => Path.GetFullPath(Path.Combine(solutionDir, project.FilePath)),
        project => project,
        StringComparer.OrdinalIgnoreCase);

    return [.. targetPaths.Select(path => ResolveTarget(path, context.Active, projectsByPath))];
  }

  public async Task SetActiveConfigurationAsync(WorkspaceBuildConfiguration configuration, CancellationToken cancellationToken = default)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;
    if (solutionFile is null)
    {
      return;
    }

    var previous = await GetActiveConfigurationAsync(cancellationToken);
    var context = await GetSolutionContextAsync(solutionFile, cancellationToken, configuration);

    if (ConfigurationEquals(previous, context.Active))
    {
      return;
    }

    ConfigurationChanged?.Invoke(new WorkspaceBuildConfigurationChangedEventArgs(
        solutionFile,
        previous,
        context.Active));
  }

  private ResolvedBuildConfiguration ResolveTarget(
      string targetPath,
      WorkspaceBuildConfiguration activeWorkspaceConfiguration,
      IReadOnlyDictionary<string, SolutionProjectModel> projectsByPath)
  {
    if (FileTypes.IsAnySolutionFile(targetPath))
    {
      return new ResolvedBuildConfiguration(
          targetPath,
          activeWorkspaceConfiguration,
          activeWorkspaceConfiguration.BuildType,
          activeWorkspaceConfiguration.Platform,
          Build: true,
          Deploy: false,
          UsedProjectMapping: false);
    }

    var fullPath = Path.GetFullPath(targetPath);
    if (!projectsByPath.TryGetValue(fullPath, out var projectModel))
    {
      return new ResolvedBuildConfiguration(
          fullPath,
          activeWorkspaceConfiguration,
          activeWorkspaceConfiguration.BuildType,
          activeWorkspaceConfiguration.Platform,
          Build: true,
          Deploy: false,
          UsedProjectMapping: false);
    }

    var (buildType, platform, build, deploy) = projectModel.GetProjectConfiguration(
        activeWorkspaceConfiguration.BuildType,
        activeWorkspaceConfiguration.Platform ?? DefaultPlatform);

    return new ResolvedBuildConfiguration(
        fullPath,
        activeWorkspaceConfiguration,
        buildType ?? activeWorkspaceConfiguration.BuildType,
        platform ?? activeWorkspaceConfiguration.Platform,
        build,
        deploy,
        UsedProjectMapping: true);
  }

  private async Task<SolutionBuildConfigurationContext> GetSolutionContextAsync(
      string solutionFile,
      CancellationToken cancellationToken,
      WorkspaceBuildConfiguration? requestedConfiguration = null)
  {
    var solutionModel = await solutionService.GetSolutionModelAsync(solutionFile, cancellationToken);
    var stored = settingsService.GetSolutionBuildConfiguration(solutionFile);

    var buildTypes = ResolveBuildTypes(solutionModel, stored);
    var platforms = ResolvePlatforms(solutionModel, stored);
    var available = CreateAvailableConfigurations(buildTypes, platforms);
    var active = ResolveActiveConfiguration(available, stored, requestedConfiguration);

    var normalized = new SolutionBuildConfigurationSettings
    {
      ActiveBuildType = active.BuildType,
      ActivePlatform = active.Platform,
      KnownBuildTypes = [.. buildTypes],
      KnownPlatforms = [.. platforms]
    };

    if (!SettingsEqual(stored, normalized))
    {
      settingsService.SetSolutionBuildConfiguration(solutionFile, normalized);
    }

    return new SolutionBuildConfigurationContext(solutionModel, active, available);
  }

  private static WorkspaceBuildConfiguration FolderDefault() => new(DebugBuildType, Platform: null);

  private static IReadOnlyList<string> ResolveBuildTypes(SolutionModel solutionModel, SolutionBuildConfigurationSettings? stored)
  {
    var current = NormalizeBuildTypes(solutionModel.BuildTypes);
    if (current.Count > 0)
    {
      return current;
    }

    var known = NormalizeBuildTypes(stored?.KnownBuildTypes);
    return known.Count > 0 ? known : [DebugBuildType, ReleaseBuildType];
  }

  private static IReadOnlyList<string> ResolvePlatforms(SolutionModel solutionModel, SolutionBuildConfigurationSettings? stored)
  {
    var current = NormalizePlatforms(solutionModel.Platforms);
    if (current.Count > 0)
    {
      return current;
    }

    var known = NormalizePlatforms(stored?.KnownPlatforms);
    return known.Count > 0 ? known : [DefaultPlatform];
  }

  private static IReadOnlyList<WorkspaceBuildConfiguration> CreateAvailableConfigurations(
      IReadOnlyList<string> buildTypes,
      IReadOnlyList<string> platforms) =>
      [.. buildTypes.SelectMany(buildType => platforms.Select(platform => new WorkspaceBuildConfiguration(buildType, platform)))];

  private static WorkspaceBuildConfiguration ResolveActiveConfiguration(
      IReadOnlyList<WorkspaceBuildConfiguration> available,
      SolutionBuildConfigurationSettings? stored,
      WorkspaceBuildConfiguration? requestedConfiguration)
  {
    if (requestedConfiguration is not null && TryFindMatchingConfiguration(available, requestedConfiguration, out var requested))
    {
      return requested;
    }

    if (stored is not null &&
        TryFindMatchingConfiguration(available, new WorkspaceBuildConfiguration(stored.ActiveBuildType ?? "", stored.ActivePlatform), out var persisted))
    {
      return persisted;
    }

    if (TryFindMatchingConfiguration(available, new WorkspaceBuildConfiguration(DebugBuildType, DefaultPlatform), out var debugAnyCpu))
    {
      return debugAnyCpu;
    }

    if (TryFindMatchingConfiguration(available, new WorkspaceBuildConfiguration(DebugBuildType, null), out var debug))
    {
      return debug;
    }

    if (TryFindMatchingConfiguration(available, new WorkspaceBuildConfiguration(ReleaseBuildType, DefaultPlatform), out var releaseAnyCpu))
    {
      return releaseAnyCpu;
    }

    return available.FirstOrDefault()
        ?? new WorkspaceBuildConfiguration(DebugBuildType, DefaultPlatform);
  }

  private static bool TryFindMatchingConfiguration(
      IReadOnlyList<WorkspaceBuildConfiguration> available,
      WorkspaceBuildConfiguration desired,
      out WorkspaceBuildConfiguration matched)
  {
    matched = available.FirstOrDefault(configuration =>
        BuildTypeEquals(configuration.BuildType, desired.BuildType)
        && PlatformEquals(configuration.Platform, desired.Platform))
        ?? available.FirstOrDefault(configuration => BuildTypeEquals(configuration.BuildType, desired.BuildType))
        ?? null!;

    return matched is not null;
  }

  private static bool SettingsEqual(
      SolutionBuildConfigurationSettings? left,
      SolutionBuildConfigurationSettings right) =>
      left is not null
      && BuildTypeEquals(left.ActiveBuildType, right.ActiveBuildType)
      && PlatformEquals(left.ActivePlatform, right.ActivePlatform)
      && SequenceEqual(left.KnownBuildTypes, right.KnownBuildTypes, BuildTypeEquals)
      && SequenceEqual(left.KnownPlatforms, right.KnownPlatforms, PlatformEquals);

  private static bool ConfigurationEquals(WorkspaceBuildConfiguration left, WorkspaceBuildConfiguration right) =>
      BuildTypeEquals(left.BuildType, right.BuildType)
      && PlatformEquals(left.Platform, right.Platform);

  private static bool SequenceEqual(
      IReadOnlyList<string>? left,
      IReadOnlyList<string>? right,
      Func<string?, string?, bool> comparer)
  {
    left ??= [];
    right ??= [];

    return left.Count == right.Count && left.Zip(right).All(pair => comparer(pair.First, pair.Second));
  }

  private static IReadOnlyList<string> NormalizeBuildTypes(IEnumerable<string>? buildTypes) =>
      [.. buildTypes?
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Select(value => value.Trim())
          .Distinct(StringComparer.OrdinalIgnoreCase)
          ?? []];

  private static IReadOnlyList<string> NormalizePlatforms(IEnumerable<string>? platforms) =>
      [.. platforms?
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Select(value => value.Trim())
          .Distinct(new PlatformComparer())
          ?? []];

  private static bool BuildTypeEquals(string? left, string? right) =>
      string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

  private static bool PlatformEquals(string? left, string? right) =>
      string.Equals(NormalizePlatformKey(left), NormalizePlatformKey(right), StringComparison.OrdinalIgnoreCase);

  private static string NormalizePlatformKey(string? platform) =>
      string.IsNullOrWhiteSpace(platform)
          ? ""
          : platform.Replace(" ", "", StringComparison.Ordinal).Trim();

  private sealed class PlatformComparer : IEqualityComparer<string>
  {
    public bool Equals(string? x, string? y) => PlatformEquals(x, y);

    public int GetHashCode(string obj) => NormalizePlatformKey(obj).ToUpperInvariant().GetHashCode();
  }

  private sealed record SolutionBuildConfigurationContext(
      SolutionModel Model,
      WorkspaceBuildConfiguration Active,
      IReadOnlyList<WorkspaceBuildConfiguration> Available);
}