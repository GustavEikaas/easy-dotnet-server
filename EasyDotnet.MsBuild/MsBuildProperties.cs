namespace EasyDotnet.MsBuild;

/// <summary>
/// Commonly used MSBuild properties.
/// </summary>
public static class MsBuildProperties
{

  public static readonly MsBuildProperty<string?> OutputPath =
      new(
          Name: "OutputPath",
          Description: "Specifies the directory for build outputs (DLL, EXE, etc.).",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> OutputType =
      new(
          Name: "OutputType",
          Description: "Specifies the type of build output to generate, such as 'Exe', 'Library', or 'WinExe'.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> TargetExt =
      new(
          Name: "TargetExt",
          Description: "Specifies the file extension of the build output, such as '.dll' or '.exe'.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<bool> IsTestProject =
      new(
          Name: "IsTestProject",
          Description: "Indicates whether the project is a test project.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> IsTestingPlatformApplication =
      new(
          Name: "IsTestingPlatformApplication",
          Description: "Indicates whether the project is intended to run as a Testing Platform application.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> AssemblyName =
      new(
          Name: "AssemblyName",
          Description: "Defines the name of the compiled assembly without its file extension. Typically matches the project name unless overridden.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> TargetFramework =
      new(
          Name: "TargetFramework",
          Description: "Specifies the target framework moniker (TFM) for the project, such as 'net8.0' or 'net6.0-windows'.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string[]?> TargetFrameworks =
      new(
          Name: "TargetFrameworks",
          Description: "Specifies multiple target framework monikers (TFMs) for multi-targeted projects, separated by semicolons (e.g., 'net6.0;net8.0').",
          Deserialize: MsBuildValueParsers.AsStringList
      );

  public static readonly MsBuildProperty<string?> UserSecretsId =
      new(
          Name: "UserSecretsId",
          Description: "Specifies the unique identifier used by the .NET User Secrets feature to locate secrets.json during development.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<bool> TestingPlatformDotnetTestSupport =
      new(
          Name: "TestingPlatformDotnetTestSupport",
          Description: "Indicates whether the project supports running tests using the 'dotnet test' command on the Testing Platform.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> TargetPath =
      new(
          Name: "TargetPath",
          Description: "Specifies the full path to the compiled output file, including the file name and extension (e.g., 'bin\\Debug\\net8.0\\MyApp.dll').",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<bool> GeneratePackageOnBuild =
      new(
          Name: "GeneratePackageOnBuild",
          Description: "Indicates whether the project should automatically generate a NuGet package when it is built.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> IsPackable =
      new(
          Name: "IsPackable",
          Description: "Indicates whether the project can be packaged into a NuGet package. False disables packing even if GeneratePackageOnBuild is true.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> PackageId =
      new(
          Name: "PackageId",
          Description: "Specifies the identifier (ID) of the NuGet package to be generated from the project.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<Version?> Version =
      new(
          Name: "Version",
          Description: "Specifies the version number of the NuGet package for the project (e.g., '1.0.0').",
          Deserialize: MsBuildValueParsers.AsVersion
      );

  public static readonly MsBuildProperty<string?> PackageOutputPath =
      new(
          Name: "PackageOutputPath",
          Description: "Specifies the directory where the generated NuGet package will be placed after building.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> TargetFrameworkVersion =
      new(
          Name: "TargetFrameworkVersion",
          Description: "Specifies the version of the target framework for the project, such as 'v4.8' for .NET Framework or null for .NET Core/NET 5+ projects.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<bool> UsingMicrosoftNETSdkWorker =
      new(
          Name: "UsingMicrosoftNETSdkWorker",
          Description: "Indicates whether the project is using the Microsoft.NET.Sdk.Worker SDK, typically for background worker applications.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> UsingMicrosoftNETSdkWeb =
      new(
          Name: "UsingMicrosoftNETSdkWeb",
          Description: "Indicates whether the project is using the Microsoft.NET.Sdk.Web SDK, typically for ASP.NET Core web applications.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> UseIISExpress =
      new(
          Name: "UseIISExpress",
          Description: "Indicates whether the project is configured to run using IIS Express for local development.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> LangVersion =
      new(
          Name: "LangVersion",
          Description: "Specifies the C# language version used by the project (e.g., '10.0', 'latest').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> RootNamespace =
      new(
          Name: "RootNamespace",
          Description: "Specifies the default root namespace for the project. Used when generating classes and resources without an explicit namespace.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<bool> IsAspireHost =
      new(
          Name: "IsAspireHost",
          Description: "Indicates whether the project is an Aspire host project.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<Version?> AspireHostingSDKVersion =
      new(
          Name: "AspireHostingSDKVersion",
          Description: "Specifies the version of the Aspire Hosting SDK applied to the project (null if using the old workload-based approach).",
          Deserialize: MsBuildValueParsers.AsVersion
      );

  public static readonly MsBuildProperty<bool> IsLegacyAspire =
      new(
          Name: "IsLegacyAspire",
          Description: "Computed property that returns true if the project is an Aspire host using the old workload-based approach (AspireHostingSDKVersion is null or less than 9.0.0).",
          Deserialize: (values, _) =>
          {
            var isHost = MsBuildValueParsers.AsBool(values, "IsAspireHost");
            var sdkVersion = MsBuildValueParsers.AsVersion(values, "AspireHostingSDKVersion");
            return isHost && (sdkVersion == null || sdkVersion < new Version(9, 0, 0));
          },
          IsComputed: true
      );

  // ## FUTURE

  public static readonly MsBuildProperty<bool> EnableDefaultCompileItems =
      new(
          Name: "EnableDefaultCompileItems",
          Description: "Indicates whether the SDK automatically includes <Compile> items for C# source files in the project.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> EnableDefaultItems =
      new(
          Name: "EnableDefaultItems",
          Description: "Indicates whether the SDK automatically includes default items (Compile, None, Content) in the project.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> EnableDefaultEmbeddedResourceItems =
      new(
          Name: "EnableDefaultEmbeddedResourceItems",
          Description: "Indicates whether the SDK automatically includes <EmbeddedResource> items for resources in the project.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> EnableDefaultNoneItems =
      new(
          Name: "EnableDefaultNoneItems",
          Description: "Indicates whether the SDK automatically includes <None> items for non-compilable, non-resource files in the project.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> IsPublishable =
      new(
          Name: "IsPublishable",
          Description: "Indicates whether the project can be published (e.g., via 'dotnet publish').",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> IsNETCoreOrNETStandard =
      new(
          Name: "_IsNETCoreOrNETStandard",
          Description: "Indicates whether the project targets .NET Core or .NET Standard frameworks.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> PublishDir =
      new(
          Name: "PublishDir",
          Description: "Specifies the directory where the project will be published.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> PublishDirName =
      new(
          Name: "PublishDirName",
          Description: "Specifies the name of the publish output directory.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> AppDesignerFolder =
      new(
          Name: "AppDesignerFolder",
          Description: "Specifies the folder that contains designer files and resources for the project (commonly 'Properties' in C# projects).",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<bool> Optimize =
      new(
          Name: "Optimize",
          Description: "Indicates whether the compiler should optimize the output assemblies for release builds.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> DebugType =
      new(
          Name: "DebugType",
          Description: "Specifies the type of debugging information to generate, such as 'Portable', 'Full', or 'PdbOnly'.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> RestoreProjectStyle =
      new(
          Name: "RestoreProjectStyle",
          Description: "Indicates the style of NuGet package restoration, e.g., 'PackageReference' for SDK-style projects.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<bool> TreatWarningsAsErrors =
      new(
          Name: "TreatWarningsAsErrors",
          Description: "Indicates whether compiler warnings should be treated as errors, causing the build to fail on warnings.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> MSBuildToolsPath =
      new(
          Name: "MSBuildToolsPath",
          Description: "Specifies the full path to the MSBuild tools directory used by the SDK.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> NetCoreTargetingPackRoot =
      new(
          Name: "NetCoreTargetingPackRoot",
          Description: "Specifies the root directory of .NET Core targeting packs installed on the system.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> NetCoreRoot =
      new(
          Name: "NetCoreRoot",
          Description: "Specifies the root directory of the installed .NET Core runtime and SDK.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> DOTNET_HOST_PATH =
      new(
          Name: "DOTNET_HOST_PATH",
          Description: "Specifies the path to the dotnet executable used to run MSBuild or other .NET commands.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> NETCoreAppMaximumVersion =
      new(
          Name: "NETCoreAppMaximumVersion",
          Description: "Specifies the maximum .NET Core App version supported by the SDK.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> BundledNETCoreAppTargetFrameworkVersion =
      new(
          Name: "BundledNETCoreAppTargetFrameworkVersion",
          Description: "Specifies the bundled target framework version for .NET Core apps provided by the SDK.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> BundledNETCoreAppPackageVersion =
      new(
          Name: "BundledNETCoreAppPackageVersion",
          Description: "Specifies the bundled package version for .NET Core apps provided by the SDK.",
          Deserialize: MsBuildValueParsers.AsString
      );


  public static readonly MsBuildProperty<string?> DefaultLanguageSourceExtension =
      new(
          Name: "DefaultLanguageSourceExtension",
          Description: "Specifies the default file extension for source code files in the project (e.g., '.cs' for C#).",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string[]?> NoWarn =
      new(
          Name: "NoWarn",
          Description: "Specifies a list of compiler warning codes to suppress, separated by semicolons (e.g., '1701;1702').",
          Deserialize: MsBuildValueParsers.AsStringList
      );

  public static readonly MsBuildProperty<string?> VisualStudioVersion =
      new(
          Name: "VisualStudioVersion",
          Description: "Specifies the version of Visual Studio used to build the project (e.g., '18.0').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> MaxSupportedLangVersion =
      new(
          Name: "MaxSupportedLangVersion",
          Description: "Specifies the maximum C# language version supported by the SDK or compiler (e.g., '13.0').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> TargetFrameworkIdentifier =
      new(
          Name: "TargetFrameworkIdentifier",
          Description: "Specifies the target framework family for the project (e.g., '.NETCoreApp', '.NETFramework').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> MSBuildProjectName =
      new(
          Name: "MSBuildProjectName",
          Description: "Specifies the name of the project file without its extension.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> ProjectDir =
      new(
          Name: "ProjectDir",
          Description: "Specifies the full directory path of the project file.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> MSBuildProjectFullPath =
      new(
          Name: "MSBuildProjectFullPath",
          Description: "Specifies the full path including file name of the project file.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> Language =
      new(
          Name: "Language",
          Description: "Specifies the programming language of the project (e.g., 'C#', 'VB', 'F#').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> BundledNETStandardTargetFrameworkVersion =
      new(
          Name: "BundledNETStandardTargetFrameworkVersion",
          Description: "Specifies the bundled .NET Standard target framework version provided by the SDK (e.g., '2.1').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> BundledNETStandardPackageVersion =
      new(
          Name: "BundledNETStandardPackageVersion",
          Description: "Specifies the bundled .NET Standard package version provided by the SDK (e.g., '2.1.0').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> BundledNETCorePlatformsPackageVersion =
      new(
          Name: "BundledNETCorePlatformsPackageVersion",
          Description: "Specifies the bundled .NET Core Platforms package version provided by the SDK (e.g., '10.0.0-preview.7.25380.108').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> BundledRuntimeIdentifierGraphFile =
      new(
          Name: "BundledRuntimeIdentifierGraphFile",
          Description: "Specifies the path to the Runtime Identifier Graph JSON file bundled with the SDK.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> NETCoreSdkVersion =
      new(
          Name: "NETCoreSdkVersion",
          Description: "Specifies the version of the .NET Core SDK being used (e.g., '10.0.100-preview.7.25380.108').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> SdkAnalysisLevel =
      new(
          Name: "SdkAnalysisLevel",
          Description: "Specifies the SDK analysis level version (e.g., '10.0.100').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> NETCoreSdkRuntimeIdentifier =
      new(
          Name: "NETCoreSdkRuntimeIdentifier",
          Description: "Specifies the runtime identifier (RID) for the SDK (e.g., 'win-x64').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> NETCoreSdkPortableRuntimeIdentifier =
      new(
          Name: "NETCoreSdkPortableRuntimeIdentifier",
          Description: "Specifies the portable runtime identifier (RID) for the SDK (e.g., 'win-x64').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<bool> NETCoreSdkIsPreview =
      new(
          Name: "_NETCoreSdkIsPreview",
          Description: "Indicates whether the current .NET Core SDK is a preview version.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> TargetsNet9 =
      new(
          Name: "TargetsNet9",
          Description: "Indicates whether the project targets .NET 9.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> TargetsNet8 =
      new(
          Name: "TargetsNet8",
          Description: "Indicates whether the project targets .NET 8.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> TargetsNet7 =
      new(
          Name: "TargetsNet7",
          Description: "Indicates whether the project targets .NET 7.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> TargetsNet6 =
      new(
          Name: "TargetsNet6",
          Description: "Indicates whether the project targets .NET 6.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> TargetsCurrent =
      new(
          Name: "TargetsCurrent",
          Description: "Indicates whether the project targets the currently installed .NET SDK version.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> IsNetCoreAppTargetingLatestTFM =
      new(
          Name: "IsNetCoreAppTargetingLatestTFM",
          Description: "Indicates whether the project is a .NET Core/NET 5+ app targeting the latest supported Target Framework Moniker (TFM).",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> RestoreTool =
      new(
          Name: "RestoreTool",
          Description: "Specifies the tool used for NuGet package restore (e.g., 'NuGet').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<bool> RestoreSuccess =
      new(
          Name: "RestoreSuccess",
          Description: "Indicates whether the last NuGet package restore operation succeeded.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> ProjectAssetsFile =
      new(
          Name: "ProjectAssetsFile",
          Description: "Specifies the full path to the project's assets file generated by NuGet restore (e.g., obj/project.assets.json).",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> NuGetPackageRoot =
      new(
          Name: "NuGetPackageRoot",
          Description: "Specifies the root directory where NuGet packages are installed.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string[]?> NuGetPackageFolders =
      new(
          Name: "NuGetPackageFolders",
          Description: "Specifies a list of directories used by NuGet to look up packages, separated by semicolons.",
          Deserialize: MsBuildValueParsers.AsStringList
      );

  public static readonly MsBuildProperty<string?> NuGetProjectStyle =
      new(
          Name: "NuGetProjectStyle",
          Description: "Specifies the style of NuGet project management (e.g., 'PackageReference').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> NuGetToolVersion =
      new(
          Name: "NuGetToolVersion",
          Description: "Specifies the version of the NuGet tool used during restore operations.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string[]?> Configurations =
      new(
          Name: "Configurations",
          Description: "Specifies the build configurations available in the project, separated by semicolons (e.g., 'Debug;Release').",
          Deserialize: MsBuildValueParsers.AsStringList
      );

  public static readonly MsBuildProperty<string?> Configuration =
      new(
          Name: "Configuration",
          Description: "Specifies the active build configuration for the current build (e.g., 'Debug' or 'Release').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string[]?> Platforms =
      new(
          Name: "Platforms",
          Description: "Specifies the target platforms available for the project, separated by semicolons (e.g., 'AnyCPU').",
          Deserialize: MsBuildValueParsers.AsStringList
      );

  public static readonly MsBuildProperty<string?> Platform =
      new(
          Name: "Platform",
          Description: "Specifies the active platform for the current build (e.g., 'AnyCPU', 'x86', 'x64').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<bool> DebugSymbols =
      new(
          Name: "DebugSymbols",
          Description: "Indicates whether the compiler should generate debug symbols (.pdb files) for the build.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> ImportDirectoryBuildProps =
      new(
          Name: "ImportDirectoryBuildProps",
          Description: "Specifies whether MSBuild automatically imports *.Directory.Build.props files.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> ImportDirectoryPackagesProps =
      new(
          Name: "ImportDirectoryPackagesProps",
          Description: "Specifies whether MSBuild automatically imports *.Directory.Packages.props files.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> MinimumMSBuildVersion =
      new(
          Name: "MinimumMSBuildVersion",
          Description: "Specifies the minimum version of MSBuild required to build the project.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> BundledMSBuildVersion =
      new(
          Name: "BundledMSBuildVersion",
          Description: "Specifies the version of MSBuild bundled with the .NET SDK used to build the project.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> MSBuildBinPath =
      new(
          Name: "MSBuildBinPath",
          Description: "Specifies the path to the MSBuild binaries used for building the project.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> DefaultAppHostRuntimeIdentifier =
      new(
          Name: "DefaultAppHostRuntimeIdentifier",
          Description: "Specifies the default runtime identifier (RID) used for publishing self-contained .NET applications.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> RunCommand =
      new(
          Name: "RunCommand",
          Description: "Specifies the command to execute the project, typically used for 'dotnet run' or custom run tasks.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> RunArguments =
      new(
          Name: "RunArguments",
          Description: "Specifies the arguments to pass to the RunCommand when executing the project.",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> ProjectDepsFileName =
      new(
          Name: "ProjectDepsFileName",
          Description: "Specifies the file name of the project's dependencies JSON file (e.g., 'MyProject.deps.json').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> ProjectDepsFilePath =
      new(
          Name: "ProjectDepsFilePath",
          Description: "Specifies the full path to the project's dependencies JSON file.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> ProjectRuntimeConfigFileName =
      new(
          Name: "ProjectRuntimeConfigFileName",
          Description: "Specifies the file name of the project's runtime configuration JSON file (e.g., 'MyProject.runtimeconfig.json').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> ProjectRuntimeConfigFilePath =
      new(
          Name: "ProjectRuntimeConfigFilePath",
          Description: "Specifies the full path to the project's runtime configuration JSON file.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> ProjectRuntimeConfigDevFilePath =
      new(
          Name: "ProjectRuntimeConfigDevFilePath",
          Description: "Specifies the full path to the development version of the project's runtime configuration JSON file.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<bool> IncludeMainProjectInDepsFile =
      new(
          Name: "IncludeMainProjectInDepsFile",
          Description: "Indicates whether the main project should be included in the generated .deps.json file.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<bool> TrimDepsJsonLibrariesWithoutAssets =
      new(
          Name: "TrimDepsJsonLibrariesWithoutAssets",
          Description: "Indicates whether libraries without runtime assets should be excluded from the .deps.json file.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> GeneratedAssemblyInfoFile =
      new(
          Name: "GeneratedAssemblyInfoFile",
          Description: "Specifies the path to the auto-generated AssemblyInfo file if GenerateAssemblyInfo is enabled.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<bool> GenerateAssemblyInfo =
      new(
          Name: "GenerateAssemblyInfo",
          Description: "Indicates whether the project is configured to generate assembly metadata (AssemblyInfo) automatically.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> TargetFileName =
      new(
          Name: "TargetFileName",
          Description: "Specifies the file name of the final build output (e.g., 'MyProject.dll' or 'MyProject.exe').",
          Deserialize: MsBuildValueParsers.AsString
      );

  public static readonly MsBuildProperty<string?> IntermediateOutputPath =
      new(
          Name: "IntermediateOutputPath",
          Description: "Specifies the path where intermediate build outputs (obj files) are stored for the project. e.g(obj/Debug/net8.0/)",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> BaseIntermediateOutputPath =
      new(
          Name: "BaseIntermediateOutputPath",
          Description: "Specifies the base directory for intermediate build outputs, usually the root folder for all configuration-specific obj directories.",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<string?> MSBuildProjectExtensionsPath =
      new(
          Name: "MSBuildProjectExtensionsPath",
          Description: "Specifies the path where MSBuild stores project-related intermediate files and extensions (commonly obj/).",
          Deserialize: MsBuildValueParsers.AsPath
      );

  public static readonly MsBuildProperty<bool> SelfContained =
      new(
          Name: "SelfContained",
          Description: "Indicates whether the project is published as a self-contained application including the .NET runtime.",
          Deserialize: MsBuildValueParsers.AsBool
      );

  public static readonly MsBuildProperty<string?> UserProfileRuntimeStorePath =
      new(
          Name: "UserProfileRuntimeStorePath",
          Description: "Specifies the directory path for the user's runtime store, used for storing precompiled runtime assets.",
          Deserialize: MsBuildValueParsers.AsPath
      );
}