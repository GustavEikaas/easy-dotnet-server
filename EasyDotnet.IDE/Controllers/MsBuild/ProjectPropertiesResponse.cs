using System;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.Controllers.MsBuild;

public sealed record DotnetProjectV1(
    string ProjectName,
    string Language,
    string? OutputType,
    string? TargetFramework,
    string[]? TargetFrameworks,
    bool IsTestProject,
    bool IsTestingPlatformApplication,
    bool IsWebProject,
    bool IsWorkerProject,
    string? TargetPath,
    bool GeneratePackageOnBuild,
    bool IsPackable,
    string? Version,
    bool IsMultiTarget,
    bool UseIISExpress
);

public static class DotnetProjectExtensions
{
  public static DotnetProjectV1 ToResponse(this DotnetProject project)
  {
    var projectName = project.MSBuildProjectName ?? throw new Exception("MSBuildProjectName can never be null");

    return new DotnetProjectV1(
        ProjectName: projectName,
        Language: GetLanguage(project.MSBuildProjectFullPath ?? throw new Exception("MSBuildProjectName can never be null")),
        OutputType: project.OutputType,
        TargetFramework: project.TargetFramework,
        TargetFrameworks: project.TargetFrameworks,
        IsTestProject: project.IsTestProject,
        IsTestingPlatformApplication: project.IsTestingPlatformApplication,
        IsWebProject: project.UsingMicrosoftNETSdkWeb,
        IsWorkerProject: project.UsingMicrosoftNETSdkWorker,
        TargetPath: project.TargetPath,
        GeneratePackageOnBuild: project.GeneratePackageOnBuild,
        IsPackable: project.IsPackable,
        Version: project.TargetFrameworkVersion,
        IsMultiTarget: project.TargetFrameworks?.Length > 1,
        UseIISExpress: project.UseIISExpress
    );
  }

  private static string GetLanguage(string path) => FileTypes.IsCsProjectFile(path) ? "csharp" : FileTypes.IsFsProjectFile(path) ? "fsharp" : "unknown";
}