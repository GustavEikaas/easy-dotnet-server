namespace EasyDotnet.BuildServer.Contracts;

public sealed record GetProjectPropertiesBatchRequest(string[] ProjectPaths, string? Configuration);

public sealed record ProjectEvaluationResult(string ProjectPath, string? Configuration, string? TargetFramework, bool Success, ValidatedDotnetProject? Project, ProjectEvaluationError? Error);

public sealed record ProjectEvaluationError(string Message, string? StackTrace, string? MsBuildErrorCode);

public record DotnetProject
(
    string? OutputPath,
    string? OutputType,
    string? OutDir,
    string? TargetExt,
    string? TargetDir,
    string? TargetName,
    bool IsTestProject,
    bool IsTestingPlatformApplication,
    string? RunSettingsFilePath,
    string? AssemblyName,
    string? TargetFramework,
    string[]? TargetFrameworks,
    string? UserSecretsId,
    bool TestingPlatformDotnetTestSupport,
    string? TargetPath,
    bool GeneratePackageOnBuild,
    bool IsPackable,
    string? PackageId,
    Version? Version,
    string? PackageOutputPath,
    string? TargetFrameworkVersion,
    bool UsingMicrosoftNETSdk,
    bool UsingGodotNETSdk,
    bool UsingMicrosoftNETSdkWorker,
    bool UsingMicrosoftNETSdkWeb,
    bool UsingMicrosoftNETSdkRazor,
    bool UsingMicrosoftNETSdkStaticWebAssets,
    bool UsingMicrosoftNETSdkBlazorWebAssembly,
    bool UseIISExpress,
    string? RunWorkingDirectory,
    string? LangVersion,
    string? RootNamespace,
    bool IsAspireHost,
    Version? AspireHostingSDKVersion,
    bool IsLegacyAspire,
    string? AspireRidToolExecutable,
    string? AspireRidToolRoot,
    string? AspireRidToolDirectory,
    string? DcpDir,
    string? DcpExtensionsDir,
    string? DcpBinDir,
    string? AspireManifestPublishOutputPath,
    string? AspireDashboardPath,
    string? AspireDashboardDir,
    string? AspirePublisher,
    bool SkipAspireWorkloadManifest,
    string? AspireGeneratedClassesVisibility,
    bool EnableDefaultCompileItems,
    bool EnableDefaultContentItems,
    bool EnableDefaultRazorGenerateItems,
    bool EnableDefaultRazorComponentItems,
    bool CopyRazorGenerateFilesToPublishDirectory,
    string? RazorCompileToolset,
    bool IncludeRazorContentInPack,
    string? RazorGenerateOutputFileExtension,
    string[]? RazorUpToDateReloadFileTypes,
    Version? RazorLangVersion,
    bool AddRazorSupportForMvc,
    string? RazorDefaultConfiguration,
    bool EnableDefaultItems,
    bool EnableDefaultEmbeddedResourceItems,
    bool EnableDefaultNoneItems,
    bool IsPublishable,
    bool IsNETCoreOrNETStandard,
    string? PublishDir,
    string? PublishDirName,
    string? AppDesignerFolder,
    bool Optimize,
    string? DebugType,
    string? RestoreProjectStyle,
    bool TreatWarningsAsErrors,
    string? MSBuildToolsPath,
    string? NetCoreTargetingPackRoot,
    string? NetCoreRoot,
    string? DOTNET_HOST_PATH,
    string? NETCoreAppMaximumVersion,
    string? BundledNETCoreAppTargetFrameworkVersion,
    string? BundledNETCoreAppPackageVersion,
    string? DefaultLanguageSourceExtension,
    string[]? NoWarn,
    string? VisualStudioVersion,
    string? MaxSupportedLangVersion,
    string? TargetFrameworkIdentifier,
    string? MSBuildProjectName,
    string? ProjectDir,
    string? ProjectName,
    string? MSBuildProjectFullPath,
    string? MicrosoftNETBuildTasksDirectoryRoot,
    string? MicrosoftNETBuildTasksDirectory,
    string? MicrosoftNETBuildTasksAssembly,
    string? MicrosoftNETBuildTasksTFM,
    bool HasRuntimeOutput,
    string? RoslynTargetsPath,
    string? RoslynTasksAssembly,
    string[]? DefaultImplicitPackages,
    string? MSBuildProjectFile,
    string? ProjectPath,
    string? Language,
    string? BundledNETStandardTargetFrameworkVersion,
    string? BundledNETStandardPackageVersion,
    string? BundledNETCorePlatformsPackageVersion,
    string? BundledRuntimeIdentifierGraphFile,
    string? NETCoreSdkVersion,
    string? SdkAnalysisLevel,
    string? NETCoreSdkRuntimeIdentifier,
    string? NETCoreSdkPortableRuntimeIdentifier,
    bool NETCoreSdkIsPreview,
    bool TargetsNet9,
    bool TargetsNet8,
    bool TargetsNet7,
    bool TargetsNet6,
    bool TargetsCurrent,
    bool IsNetCoreAppTargetingLatestTFM,
    string? RestoreTool,
    bool RestoreSuccess,
    string? ProjectAssetsFile,
    string? NuGetPackageRoot,
    string[]? NuGetPackageFolders,
    string? NuGetProjectStyle,
    string? NuGetToolVersion,
    string[]? Configurations,
    string? Configuration,
    string[]? Platforms,
    string? Platform,
    bool DebugSymbols,
    bool ImportDirectoryBuildProps,
    bool ImportDirectoryPackagesProps,
    string? MinimumMSBuildVersion,
    string? BundledMSBuildVersion,
    string? MSBuildBinPath,
    string? DefaultAppHostRuntimeIdentifier,
    string? RunCommand,
    string? RunArguments,
    string? ProjectDepsFileName,
    string? ProjectDepsFilePath,
    string? ProjectRuntimeConfigFileName,
    string? ProjectRuntimeConfigFilePath,
    string? ProjectRuntimeConfigDevFilePath,
    bool IncludeMainProjectInDepsFile,
    bool TrimDepsJsonLibrariesWithoutAssets,
    string? GeneratedAssemblyInfoFile,
    bool GenerateAssemblyInfo,
    string? TargetFileName,
    string? IntermediateOutputPath,
    string? BaseIntermediateOutputPath,
    string? MSBuildProjectExtensionsPath,
    bool SelfContained,
    string? UserProfileRuntimeStorePath,
    string? TargetPlatformIdentifier
);

public sealed record ValidatedDotnetProject
{
  public required string TargetFramework { get; init; }
  public required string OutputType { get; init; }
  public required string ProjectPath { get; init; }
  public required string ProjectFullPath { get; init; }
  public required string ProjectName { get; init; }
  public required string AssemblyName { get; init; }
  public required string TargetPath { get; init; }
  public required DotnetProject Raw { get; init; }

  public bool IsRunnable =>
      OutputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
      && !Raw.IsTestProject
      && !Raw.IsTestingPlatformApplication;

  public bool IsMTP => Raw.IsTestingPlatformApplication;

  public bool IsVsTest => !Raw.IsTestingPlatformApplication && Raw.IsTestProject;

  public bool IsPackable => Raw.IsPackable
      && !string.IsNullOrWhiteSpace(Raw.PackageId);

  public string? LaunchSettingsPath
  {
    get
    {
      var baseDir = File.Exists(ProjectFullPath)
          ? Path.GetDirectoryName(ProjectFullPath)
          : ProjectFullPath;
      return Path.Combine(baseDir, "Properties", "launchSettings.json");
    }
  }

  public static ValidatedDotnetProject? TryCreate(DotnetProject project) =>
      project switch
      {
        {
          TargetFramework: { } tfm,
          OutputType: { } outputType,
          ProjectPath: { } projectPath,
          MSBuildProjectFullPath: { } fullPath,
          AssemblyName: { } assemblyName,
          TargetPath: { } targetPath,
          MSBuildProjectName: { } projectName
        } => new ValidatedDotnetProject
        {
          TargetFramework = tfm,
          OutputType = outputType,
          ProjectPath = projectPath,
          ProjectFullPath = fullPath,
          ProjectName = projectName,
          AssemblyName = assemblyName,
          TargetPath = targetPath,
          Raw = project
        },
        _ => null
      };
}

public enum DotnetPlatform
{
  None,
  Android,
  iOS,
  MacCatalyst,
  MacOS,
  TvOS,
  Tizen,
  Browser,
  Windows,
  Unknown
}

public static class DotnetProjectTfmExtensions
{
  public static string? GetTfmPlatform(this DotnetProject project)
  {
    var tfm = project.TargetFramework;
    if (string.IsNullOrWhiteSpace(tfm))
      return null;

    var dashIndex = tfm!.IndexOf('-');
    if (dashIndex < 0 || dashIndex == tfm.Length - 1)
      return null;

    return tfm[(dashIndex + 1)..];
  }
}

public static class DotnetProjectPlatformExtensions
{
  public static DotnetPlatform GetPlatform(this DotnetProject project)
  {
    var platform = project.GetTfmPlatform()?.ToLowerInvariant();

    return platform switch
    {
      null => DotnetPlatform.None,
      var p when p.StartsWith("android") => DotnetPlatform.Android,
      var p when p.StartsWith("ios") => DotnetPlatform.iOS,
      var p when p.StartsWith("maccatalyst") => DotnetPlatform.MacCatalyst,
      var p when p.StartsWith("macos") => DotnetPlatform.MacOS,
      var p when p.StartsWith("tvos") => DotnetPlatform.TvOS,
      var p when p.StartsWith("tizen") => DotnetPlatform.Tizen,
      var p when p.StartsWith("browser") => DotnetPlatform.Browser,
      var p when p.StartsWith("windows") => DotnetPlatform.Windows,
      _ => DotnetPlatform.Unknown
    };
  }
}