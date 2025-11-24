using System;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.Controllers.MsBuild;

public sealed record DotnetProjectV1(
    string ProjectName,
    string Language,
    string? OutputPath,
    string? OutputType,
    string? TargetExt,
    string? AssemblyName,
    string? TargetFramework,
    string[]? TargetFrameworks,
    bool IsTestProject,
    bool IsWebProject,
    bool IsWorkerProject,
    string? UserSecretsId,
    bool TestingPlatformDotnetTestSupport,
    bool IsTestingPlatformApplication,
    string? TargetPath,
    bool GeneratePackageOnBuild,
    bool IsPackable,
    string? LangVersion,
    string? RootNamespace,
    string? PackageId,
    string? NugetVersion,
    string? Version,
    string? PackageOutputPath,
    bool IsMultiTarget,
    bool IsNetFramework,
    bool UseIISExpress,
    string RunCommand,
    string BuildCommand,
    string TestCommand,
    bool IsAspireHost,
    Version? AspireHostingSdkVersion
);

public static class DotnetProjectExtensions
{
  public static DotnetProjectV1 ToResponse(
      this DotnetProject project,
      string runCommand,
      string buildCommand,
      string testCommand)
  {
    var nugetVersion = project.Version?.ToString();
    var targetFrameworkVersion = project.TargetFrameworkVersion;
    var isNetFramework = targetFrameworkVersion?.StartsWith("v4") == true;
    var useIISExpress = project.UseIISExpress;
    var targetPath = project.TargetPath;
    var projectName = project.MSBuildProjectName ?? throw new Exception("MSBuildProjectName can never be null");
    var aspireSdkVersion = project.AspireHostingSDKVersion;

    return new DotnetProjectV1(
        ProjectName: projectName,
        Language: GetLanguage(project.MSBuildProjectFullPath ?? throw new Exception("MSBuildProjectName can never be null")),
        OutputPath: project.OutputPath,
        OutputType: project.OutputType,
        TargetExt: project.TargetExt,
        AssemblyName: project.AssemblyName,
        TargetFramework: project.TargetFramework,
        TargetFrameworks: project.TargetFrameworks,
        IsTestProject: project.IsTestProject,
        IsWebProject: project.UsingMicrosoftNETSdkWeb,
        IsWorkerProject: project.UsingMicrosoftNETSdkWorker,
        UserSecretsId: project.UserSecretsId,
        TestingPlatformDotnetTestSupport: project.TestingPlatformDotnetTestSupport,
        IsTestingPlatformApplication: project.IsTestingPlatformApplication,
        TargetPath: targetPath,
        GeneratePackageOnBuild: project.GeneratePackageOnBuild,
        IsPackable: project.IsPackable,
        LangVersion: project.LangVersion,
        RootNamespace: project.RootNamespace,
        PackageId: project.PackageId,
        NugetVersion: string.IsNullOrWhiteSpace(nugetVersion) ? null : nugetVersion,
        Version: targetFrameworkVersion,
        PackageOutputPath: project.PackageOutputPath,
        IsMultiTarget: project.TargetFrameworks?.Length > 1,
        IsNetFramework: isNetFramework,
        UseIISExpress: useIISExpress,
        RunCommand: runCommand,
        BuildCommand: buildCommand,
        TestCommand: testCommand,
        IsAspireHost: project.IsAspireHost,
        AspireHostingSdkVersion: aspireSdkVersion
    );
  }

  private static string GetLanguage(string path) => FileTypes.IsCsProjectFile(path) ? "csharp" : FileTypes.IsFsProjectFile(path) ? "fsharp" : "unknown";
}