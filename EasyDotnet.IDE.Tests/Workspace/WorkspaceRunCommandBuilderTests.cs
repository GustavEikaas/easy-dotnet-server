using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Workspace.Services;

namespace EasyDotnet.IDE.Tests.Workspace;

public sealed class WorkspaceRunCommandBuilderTests
{
  [Test]
  public async Task Build_UsesSdkRunCommandAndParsesRunArguments()
  {
    var project = CreateProject(
        runCommand: "dotnet",
        runArguments: "exec \"/tmp/my app/App.dll\"",
        useAppHost: false);

    var command = WorkspaceRunCommandBuilder.Build(project, null, ["--flag", "value"], null);

    await Assert.That(command.Executable).IsEqualTo("dotnet");
    await Assert.That(command.Arguments).IsEquivalentTo(["exec", "/tmp/my app/App.dll", "--flag", "value"]);
  }

  [Test]
  public async Task Build_UsesAppHostWhenSdkComputedAppHost()
  {
    var project = CreateProject(
        runCommand: "/tmp/my app/App",
        runArguments: "",
        useAppHost: true,
        targetPath: "/tmp/my app/App.dll");

    var command = WorkspaceRunCommandBuilder.Build(project, null, ["user-arg"], null);

    await Assert.That(command.Executable).IsEqualTo("/tmp/my app/App");
    await Assert.That(command.Arguments).IsEquivalentTo(["user-arg"]);
  }

  [Test]
  public async Task Build_FallsBackToDotnetExecForNetCoreDllWhenRunCommandMissing()
  {
    var project = CreateProject(
        runCommand: null,
        runArguments: null,
        targetPath: "/tmp/App.dll",
        useAppHost: false);

    var command = WorkspaceRunCommandBuilder.Build(project, null, null, null);

    await Assert.That(command.Executable).IsEqualTo("dotnet");
    await Assert.That(command.Arguments).IsEquivalentTo(["exec", "/tmp/App.dll"]);
  }

  [Test]
  public async Task Build_RejectsWindowsAppHostOnNonWindows()
  {
    if (OperatingSystem.IsWindows())
    {
      return;
    }

    var project = CreateProject(
        runCommand: "/tmp/App.exe",
        runtimeIdentifier: "win-x64",
        useAppHost: true);

    var ex = Assert.Throws<InvalidOperationException>(() =>
        WorkspaceRunCommandBuilder.Build(project, null, null, null));

    await Assert.That(ex.Message).Contains("win-x64");
  }

  [Test]
  public async Task ValidatedProject_WinExeWithRuntimeOutput_IsRunnable()
  {
    var project = CreateProject(
        outputType: "WinExe",
        runCommand: "/tmp/App",
        useAppHost: true,
        hasRuntimeOutput: true);

    var validated = ValidatedDotnetProject.TryCreate(project);

    await Assert.That(validated).IsNotNull();
    await Assert.That(validated!.IsRunnable).IsTrue();
  }

  private static DotnetProject CreateProject(
      string outputType = "Exe",
      string? runCommand = "/tmp/App",
      string? runArguments = "",
      string targetPath = "/tmp/App.dll",
      bool useAppHost = false,
      bool hasRuntimeOutput = true,
      string? runtimeIdentifier = null)
    => new(
        OutputPath: "/tmp/bin/Debug/net8.0/",
        OutputType: outputType,
        OutDir: "/tmp/bin/Debug/net8.0/",
        TargetExt: ".dll",
        TargetDir: "/tmp/bin/Debug/net8.0/",
        TargetName: "App",
        IsTestProject: false,
        IsTestingPlatformApplication: false,
        RunSettingsFilePath: null,
        AssemblyName: "App",
        TargetFramework: "net8.0",
        TargetFrameworks: null,
        UserSecretsId: null,
        TestingPlatformDotnetTestSupport: false,
        TargetPath: targetPath,
        GeneratePackageOnBuild: false,
        IsPackable: false,
        PackageId: null,
        Version: null,
        PackageOutputPath: null,
        TargetFrameworkVersion: "v8.0",
        UsingMicrosoftNETSdk: true,
        UsingGodotNETSdk: false,
        UsingMicrosoftNETSdkWorker: false,
        UsingMicrosoftNETSdkWeb: false,
        UsingMicrosoftNETSdkRazor: false,
        UsingMicrosoftNETSdkStaticWebAssets: false,
        UsingMicrosoftNETSdkBlazorWebAssembly: false,
        UseIISExpress: false,
        RunWorkingDirectory: null,
        LangVersion: null,
        RootNamespace: "App",
        IsAspireHost: false,
        AspireHostingSDKVersion: null,
        IsLegacyAspire: false,
        AspireRidToolExecutable: null,
        AspireRidToolRoot: null,
        AspireRidToolDirectory: null,
        DcpDir: null,
        DcpExtensionsDir: null,
        DcpBinDir: null,
        AspireManifestPublishOutputPath: null,
        AspireDashboardPath: null,
        AspireDashboardDir: null,
        AspirePublisher: null,
        SkipAspireWorkloadManifest: false,
        AspireGeneratedClassesVisibility: null,
        EnableDefaultCompileItems: true,
        EnableDefaultContentItems: true,
        EnableDefaultRazorGenerateItems: true,
        EnableDefaultRazorComponentItems: true,
        CopyRazorGenerateFilesToPublishDirectory: false,
        RazorCompileToolset: null,
        IncludeRazorContentInPack: false,
        RazorGenerateOutputFileExtension: null,
        RazorUpToDateReloadFileTypes: null,
        RazorLangVersion: null,
        AddRazorSupportForMvc: false,
        RazorDefaultConfiguration: null,
        EnableDefaultItems: true,
        EnableDefaultEmbeddedResourceItems: true,
        EnableDefaultNoneItems: true,
        IsPublishable: true,
        IsNETCoreOrNETStandard: true,
        PublishDir: null,
        PublishDirName: null,
        AppDesignerFolder: "Properties",
        Optimize: false,
        DebugType: "portable",
        RestoreProjectStyle: "PackageReference",
        TreatWarningsAsErrors: false,
        MSBuildToolsPath: null,
        NetCoreTargetingPackRoot: null,
        NetCoreRoot: null,
        DOTNET_HOST_PATH: null,
        NETCoreAppMaximumVersion: null,
        BundledNETCoreAppTargetFrameworkVersion: null,
        BundledNETCoreAppPackageVersion: null,
        DefaultLanguageSourceExtension: ".cs",
        NoWarn: null,
        VisualStudioVersion: null,
        MaxSupportedLangVersion: null,
        TargetFrameworkIdentifier: ".NETCoreApp",
        MSBuildProjectName: "App",
        ProjectDir: "/tmp/",
        ProjectName: "App",
        MSBuildProjectFullPath: "/tmp/App.csproj",
        MicrosoftNETBuildTasksDirectoryRoot: null,
        MicrosoftNETBuildTasksDirectory: null,
        MicrosoftNETBuildTasksAssembly: null,
        MicrosoftNETBuildTasksTFM: null,
        HasRuntimeOutput: hasRuntimeOutput,
        RoslynTargetsPath: null,
        RoslynTasksAssembly: null,
        DefaultImplicitPackages: null,
        MSBuildProjectFile: "App.csproj",
        ProjectPath: "/tmp/App.csproj",
        Language: "C#",
        BundledNETStandardTargetFrameworkVersion: null,
        BundledNETStandardPackageVersion: null,
        BundledNETCorePlatformsPackageVersion: null,
        BundledRuntimeIdentifierGraphFile: null,
        NETCoreSdkVersion: null,
        SdkAnalysisLevel: null,
        NETCoreSdkRuntimeIdentifier: "linux-x64",
        NETCoreSdkPortableRuntimeIdentifier: "linux-x64",
        NETCoreSdkIsPreview: false,
        TargetsNet9: false,
        TargetsNet8: true,
        TargetsNet7: false,
        TargetsNet6: false,
        TargetsCurrent: false,
        IsNetCoreAppTargetingLatestTFM: false,
        RestoreTool: null,
        RestoreSuccess: true,
        ProjectAssetsFile: null,
        NuGetPackageRoot: null,
        NuGetPackageFolders: null,
        NuGetProjectStyle: "PackageReference",
        NuGetToolVersion: null,
        Configurations: ["Debug"],
        Configuration: "Debug",
        Platforms: null,
        Platform: null,
        DebugSymbols: true,
        ImportDirectoryBuildProps: true,
        ImportDirectoryPackagesProps: true,
        MinimumMSBuildVersion: null,
        BundledMSBuildVersion: null,
        MSBuildBinPath: null,
        DefaultAppHostRuntimeIdentifier: "linux-x64",
        RuntimeIdentifier: runtimeIdentifier,
        UseAppHost: useAppHost,
        RunCommand: runCommand,
        RunArguments: runArguments,
        ProjectDepsFileName: null,
        ProjectDepsFilePath: null,
        ProjectRuntimeConfigFileName: null,
        ProjectRuntimeConfigFilePath: null,
        ProjectRuntimeConfigDevFilePath: null,
        IncludeMainProjectInDepsFile: false,
        TrimDepsJsonLibrariesWithoutAssets: false,
        GeneratedAssemblyInfoFile: null,
        GenerateAssemblyInfo: true,
        TargetFileName: "App.dll",
        IntermediateOutputPath: "/tmp/obj/Debug/net8.0/",
        BaseIntermediateOutputPath: "/tmp/obj/",
        MSBuildProjectExtensionsPath: "/tmp/obj/",
        SelfContained: false,
        UserProfileRuntimeStorePath: null,
        TargetPlatformIdentifier: null);
}