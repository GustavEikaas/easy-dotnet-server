using System.IO.Abstractions;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Models.LaunchProfile;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Settings;
using EasyDotnet.IDE.Workspace.BuildConfiguration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.IDE.Tests.Workspace.BuildConfiguration;

public sealed class WorkspaceBuildConfigurationServiceTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"easy-dotnet-build-config-{Guid.NewGuid():N}");
  private readonly string _settingsRoot;

  public WorkspaceBuildConfigurationServiceTests()
  {
    _settingsRoot = Path.Combine(_tempRoot, "settings");
    Directory.CreateDirectory(_tempRoot);
    Directory.CreateDirectory(_settingsRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, recursive: true);
    }
  }

  [Test]
  public async Task NoSolutionWorkspace_FallsBackToDebug()
  {
    using var harness = CreateHarness(solutionFile: null);

    var active = await harness.Service.GetActiveConfigurationAsync();
    var available = await harness.Service.GetAvailableConfigurationsAsync();

    await Assert.That(active.BuildType).IsEqualTo("Debug");
    await Assert.That(active.Platform).IsNull();
    await Assert.That(available.Count).IsEqualTo(1);
    await Assert.That(available[0].BuildType).IsEqualTo("Debug");
    await Assert.That(available[0].Platform).IsNull();
  }

  [Test]
  public async Task MissingSolutionConfigurations_BootstrapsDebugReleaseAnyCpuAndPersistsSnapshot()
  {
    var solutionFile = CreateSolutionFile(
        "Bootstrap.sln",
        [CreateProject("AppAlpha")],
        configurationSection: null,
        projectConfigurationSection: null);

    using var harness = CreateHarness(solutionFile);

    var active = await harness.Service.GetActiveConfigurationAsync();
    var available = await harness.Service.GetAvailableConfigurationsAsync();
    var stored = harness.Settings.GetSolutionBuildConfiguration(solutionFile);

    await Assert.That(active.BuildType).IsEqualTo("Debug");
    await Assert.That(active.Platform).IsEqualTo("Any CPU");
    await Assert.That(available.Select(x => x.DisplayName).ToArray())
        .IsEquivalentTo(["Debug|Any CPU", "Release|Any CPU"]);
    await Assert.That(stored).IsNotNull();
    await Assert.That(stored!.KnownBuildTypes).IsEquivalentTo(["Debug", "Release"]);
    await Assert.That(stored.KnownPlatforms).IsEquivalentTo(["Any CPU"]);
  }

  [Test]
  public async Task InvalidPersistedConfiguration_SelfHealsToDefault()
  {
    var project = CreateProject("AppAlpha");
    var solutionFile = CreateSolutionFile(
        "Healing.sln",
        [project],
        configurationSection:
        [
          "\t\tDebug|Any CPU = Debug|Any CPU",
          "\t\tRelease|Any CPU = Release|Any CPU"
        ],
        projectConfigurationSection:
        [
          $"\t\t{{{project.ProjectGuid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Release|Any CPU.Build.0 = Release|Any CPU"
        ]);

    using var harness = CreateHarness(solutionFile);
    harness.Settings.SetSolutionBuildConfiguration(solutionFile, new SolutionBuildConfigurationSettings
    {
      ActiveBuildType = "Custom",
      ActivePlatform = "x64",
      KnownBuildTypes = ["Custom"],
      KnownPlatforms = ["x64"]
    });

    var active = await harness.Service.GetActiveConfigurationAsync();
    var stored = harness.Settings.GetSolutionBuildConfiguration(solutionFile);

    await Assert.That(active.DisplayName).IsEqualTo("Debug|Any CPU");
    await Assert.That(stored).IsNotNull();
    await Assert.That(stored!.ActiveBuildType).IsEqualTo("Debug");
    await Assert.That(stored.ActivePlatform).IsEqualTo("Any CPU");
  }

  [Test]
  public async Task ResolveTargetAsync_UsesSolutionProjectConfigurationMapping()
  {
    var project = CreateProject("AppAlpha");
    var solutionFile = CreateSolutionFile(
        "Mapped.sln",
        [project],
        configurationSection:
        [
          "\t\tDebug|Any CPU = Debug|Any CPU",
          "\t\tRelease|Any CPU = Release|Any CPU"
        ],
        projectConfigurationSection:
        [
          $"\t\t{{{project.ProjectGuid}}}.Debug|Any CPU.ActiveCfg = Release|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Debug|Any CPU.Build.0 = Release|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Release|Any CPU.Build.0 = Release|Any CPU"
        ]);

    using var harness = CreateHarness(solutionFile);

    var resolved = await harness.Service.ResolveTargetAsync(project.ProjectPath);

    await Assert.That(resolved.Configuration).IsEqualTo("Release");
    await Assert.That(resolved.Platform).IsEqualTo("Any CPU");
    await Assert.That(resolved.UsedProjectMapping).IsTrue();
  }

  [Test]
  public async Task SetActiveConfigurationAsync_PersistsAndRaisesChangeEvent()
  {
    var project = CreateProject("AppAlpha");
    var solutionFile = CreateSolutionFile(
        "Switching.sln",
        [project],
        configurationSection:
        [
          "\t\tDebug|Any CPU = Debug|Any CPU",
          "\t\tRelease|Any CPU = Release|Any CPU"
        ],
        projectConfigurationSection:
        [
          $"\t\t{{{project.ProjectGuid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU",
          $"\t\t{{{project.ProjectGuid}}}.Release|Any CPU.Build.0 = Release|Any CPU"
        ]);

    using var harness = CreateHarness(solutionFile);
    WorkspaceBuildConfigurationChangedEventArgs? received = null;
    harness.Service.ConfigurationChanged += args => received = args;

    await harness.Service.SetActiveConfigurationAsync(new WorkspaceBuildConfiguration("Release", "Any CPU"));

    var active = await harness.Service.GetActiveConfigurationAsync();
    var stored = harness.Settings.GetSolutionBuildConfiguration(solutionFile);

    await Assert.That(received).IsNotNull();
    await Assert.That(received!.Previous.DisplayName).IsEqualTo("Debug|Any CPU");
    await Assert.That(received.Current.DisplayName).IsEqualTo("Release|Any CPU");
    await Assert.That(active.DisplayName).IsEqualTo("Release|Any CPU");
    await Assert.That(stored!.ActiveBuildType).IsEqualTo("Release");
    await Assert.That(stored.ActivePlatform).IsEqualTo("Any CPU");
  }

  private Harness CreateHarness(string? solutionFile)
  {
    var rootDir = solutionFile is null ? _tempRoot : Path.GetDirectoryName(solutionFile)!;
    var clientService = new FakeClientService
    {
      ProjectInfo = new ProjectInfo(rootDir, solutionFile)
    };

    var fileSystem = new FileSystem();
    var resolver = new SettingsFileResolver(fileSystem, _settingsRoot);
    var serializer = new SettingsSerializer(fileSystem, NullLogger<SettingsSerializer>.Instance);
    var settings = new SettingsService(
        fileSystem,
        resolver,
        serializer,
        clientService,
        new NoOpLaunchProfileService(),
        new NoOpBuildHostManager(),
        new NoOpNotificationService(),
        NullLogger<SettingsService>.Instance);

    var solutionService = new SolutionService();
    var service = new WorkspaceBuildConfigurationService(clientService, solutionService, settings);
    return new Harness(service, settings);
  }

  private ProjectFixture CreateProject(string projectName)
  {
    var projectDir = Path.Combine(_tempRoot, projectName);
    Directory.CreateDirectory(projectDir);
    var projectPath = Path.Combine(projectDir, $"{projectName}.csproj");
    File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    return new ProjectFixture(projectName, projectPath, Guid.NewGuid().ToString().ToUpperInvariant());
  }

  private string CreateSolutionFile(
      string fileName,
      IReadOnlyList<ProjectFixture> projects,
      IReadOnlyList<string>? configurationSection,
      IReadOnlyList<string>? projectConfigurationSection)
  {
    var solutionPath = Path.Combine(_tempRoot, fileName);
    var projectEntries = string.Join(
        Environment.NewLine,
        projects.Select(project =>
            $@"Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{project.ProjectName}"", ""{project.ProjectPath}"", ""{{{project.ProjectGuid}}}""
EndProject"));

    var configurationBlock = configurationSection is null
        ? ""
        : $@"
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
{string.Join(Environment.NewLine, configurationSection)}
	EndGlobalSection";

    var projectConfigurationBlock = projectConfigurationSection is null
        ? ""
        : $@"
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
{string.Join(Environment.NewLine, projectConfigurationSection)}
	EndGlobalSection";

    var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
{projectEntries}
Global{configurationBlock}{projectConfigurationBlock}
EndGlobal
";

    File.WriteAllText(solutionPath, solutionContent.TrimStart());
    return solutionPath;
  }

  private sealed record Harness(
      WorkspaceBuildConfigurationService Service,
      SettingsService Settings) : IDisposable
  {
    public void Dispose() { }
  }

  private sealed record ProjectFixture(string ProjectName, string ProjectPath, string ProjectGuid);

  private sealed class FakeClientService : IClientService
  {
    public bool IsInitialized { get; set; }
    public bool UseVisualStudio { get; set; }
    public bool HasExternalTerminal => false;
    public bool SupportsSingleFileExecution { get; set; }
    public ProjectInfo? ProjectInfo { get; set; }
    public ClientInfo? ClientInfo { get; set; }
    public ClientOptions? ClientOptions { get; set; }

    public void ThrowIfNotInitialized() { }

    public string RequireSolutionFile() => ProjectInfo?.SolutionFile ?? throw new InvalidOperationException();

    public string RequireRootDir() => ProjectInfo?.RootDir ?? throw new InvalidOperationException();
  }

  private sealed class NoOpLaunchProfileService : ILaunchProfileService
  {
    public LaunchProfile? GetLaunchProfile(string targetPath, string? profileName) => null;

    public Dictionary<string, LaunchProfile>? GetLaunchProfiles(string targetPath) => null;
  }

  private sealed class NoOpNotificationService : INotificationService
  {
    public Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug") => Task.CompletedTask;

    public Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType) => Task.CompletedTask;

    public Task NotifyActiveProjectChanged(string? projectPath, string? projectName, string? launchProfile) => Task.CompletedTask;

    public Task NotifyRunningProcessesChangedAsync(RunningSessionInfo[] projects) => Task.CompletedTask;
  }

  private sealed class NoOpBuildHostManager : IBuildHostManager
  {
    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatchAsync(GetProjectPropertiesBatchRequest request, CancellationToken cancellationToken) =>
        EmptyProjectEvaluations();

    public Task<GetWatchListResponse> GetProjectWatchListAsync(GetWatchListRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new GetWatchListResponse([]));

    public IAsyncEnumerable<RestoreResult> RestoreNugetPackagesAsync(RestoreRequest request, CancellationToken cancellationToken) =>
        EmptyRestoreResults();

    public IAsyncEnumerable<BatchBuildResult> BatchBuildAsync(BatchBuildRequest request, CancellationToken cancellationToken) =>
        EmptyBuildResults();

    public Task<ConvertSingleFileResponse> ConvertFileToProjectAsync(string entryPointFilePath, CancellationToken cancellationToken) =>
        Task.FromResult(new ConvertSingleFileResponse("", new ProjectEvaluationResult(entryPointFilePath, null, null, false, null, null)));

    public Task<BuildServerDiagnosticsResponse> GetBuildServerDiagnosticsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BuildServerDiagnosticsResponse("", 0, "", ""));

    public Task<InstalledPackageReference[]> ListPackageReferencesAsync(string projectPath, CancellationToken cancellationToken) =>
        Task.FromResult(Array.Empty<InstalledPackageReference>());

    public Task SetLogLevelAsync(string level, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string[]> GetLogsAsync(CancellationToken cancellationToken) => Task.FromResult(Array.Empty<string>());

    private static async IAsyncEnumerable<ProjectEvaluationResult> EmptyProjectEvaluations()
    {
      yield break;
    }

    private static async IAsyncEnumerable<RestoreResult> EmptyRestoreResults()
    {
      yield break;
    }

    private static async IAsyncEnumerable<BatchBuildResult> EmptyBuildResults()
    {
      yield break;
    }
  }
}
