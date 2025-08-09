namespace EasyDotnet.MsBuild.Contracts;

public sealed record DotnetProjectProperties(
  string OutputPath,
  string? OutputType,
  string? TargetExt,
  string? AssemblyName,
  string? TargetFramework,
  string[]? TargetFrameworks,
  bool IsTestProject,
  bool IsMultiTarget,
  string? UserSecretsId,
  bool TestingPlatformDotnetTestSupport,
  string? TargetPath,
  bool GeneratePackageOnBuild,
  bool IsPackable,
  string? PackageId,
  string? NugetVersion,
  string? Version,
  string? PackageOutputPath,
  List<MsBuildSource> Sources,
  DateTime CacheTime
);