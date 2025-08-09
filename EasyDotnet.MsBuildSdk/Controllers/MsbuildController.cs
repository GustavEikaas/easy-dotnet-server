using EasyDotnet.MsBuild.Contracts;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using StreamJsonRpc;

namespace EasyDotnet.MsBuildSdk.Controllers;

public class MsbuildController
{

  [JsonRpcMethod("msbuild/build")]
  public static MsBuild.Contracts.BuildResult RequestBuild(string targetPath, string configuration)
  {
    var properties = new Dictionary<string, string?> { { "Configuration", configuration } };

    using var pc = new ProjectCollection();
    var buildRequest = new BuildRequestData(targetPath, properties, null, ["Restore", "Build"], null);
    var logger = new InMemoryLogger();

    var parameters = new BuildParameters(pc) { Loggers = [logger] };

    var result = BuildManager.DefaultBuildManager.Build(parameters, buildRequest);

    return new MsBuild.Contracts.BuildResult(Success: result.OverallResult == BuildResultCode.Success, logger.Errors, logger.Warnings);
  }

  [JsonRpcMethod("msbuild/query-project-properties")]
  public static DotnetProjectProperties QueryProject(string targetPath, string configuration, string? targetFramework)
  {
    var properties = new Dictionary<string, string> { { "Configuration", configuration } };
    if (!string.IsNullOrEmpty(targetFramework))
    {
      properties.Add("TargetFramework", targetFramework);
    }
    using var pc = new ProjectCollection();

    var project = pc.LoadProject(targetPath);
    project.ReevaluateIfNecessary();

    var targetFrameworkValue = StringOrNull(project, "TargetFramework");
    var targetFrameworksValue = StringOrNull(project, "TargetFrameworks");

    var versionValue = !string.IsNullOrEmpty(targetFrameworkValue)
        ? targetFrameworkValue.Replace("net", "")
        : !string.IsNullOrEmpty(targetFrameworksValue)
            ? string.Join(";", targetFrameworksValue.Split(';').Select(tf => tf.Replace("net", "")))
            : null;

    var sdkPath = Environment.GetEnvironmentVariable("DOTNET_ROOT")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

    var sdkPathNormalized = Path.GetFullPath(sdkPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    var sources = project.Imports
        .Where(x => !Path.GetFullPath(x.ImportedProject.FullPath)
            .StartsWith(sdkPathNormalized, StringComparison.OrdinalIgnoreCase))
        .Select(x => new MsBuildSource(
            x.ImportedProject.FullPath.Replace("\\", "/"),
            x.ImportedProject.LastWriteTimeWhenRead))
        .Append(new MsBuildSource(targetPath, File.GetLastWriteTime(targetPath)))
        .ToList();

    return new DotnetProjectProperties(
        OutputPath: project.GetPropertyValue("OutputPath")?.Replace("\\", "/") ?? throw new Exception("Output path cannot be null"),
        OutputType: project.GetPropertyValue("OutputType"),
        TargetExt: StringOrNull(project, "TargetExt"),
        AssemblyName: project.GetPropertyValue("AssemblyName"),
        TargetFramework: targetFrameworkValue,
        TargetFrameworks: !string.IsNullOrEmpty(targetFrameworksValue) ? targetFrameworksValue?.Split(";") : null,
        IsTestProject: GetBoolProperty(project, "IsTestProject"),
        UserSecretsId: StringOrNull(project, "UserSecretsId"),
        TestingPlatformDotnetTestSupport: GetBoolProperty(project, "TestingPlatformDotnetTestSupport"),
        TargetPath: StringOrNull(project, "TargetPath"),
        GeneratePackageOnBuild: GetBoolProperty(project, "GeneratePackageOnBuild"),
        IsPackable: GetBoolProperty(project, "IsPackable"),
        PackageId: project.GetPropertyValue("PackageId"),
        NugetVersion: project.GetPropertyValue("Version"),
        Version: versionValue,
        IsMultiTarget: !string.IsNullOrEmpty(targetFrameworksValue),
        PackageOutputPath: project.GetPropertyValue("PackageOutputPath")?.Replace("\\", "/") ?? "",
        Sources: sources,
        CacheTime: DateTime.Now
    );
  }

  private static bool GetBoolProperty(Project project, string name) =>
    string.Equals(project.GetPropertyValue(name), "true", StringComparison.OrdinalIgnoreCase);

  private static string? StringOrNull(Project project, string name)
  {
    var value = project.GetPropertyValue(name);
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }
}


public class InMemoryLogger : ILogger
{
  public List<BuildMessage> Errors { get; } = [];
  public List<BuildMessage> Warnings { get; } = [];

  public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;
  public string? Parameters { get; set; }

  public void Initialize(IEventSource eventSource)
  {
    eventSource.ErrorRaised += (sender, args) => Errors.Add(new BuildMessage("error", args.File, args.LineNumber, args.ColumnNumber, args.Code, args?.Message));
    eventSource.WarningRaised += (sender, args) => Warnings.Add(new BuildMessage("warning", args.File, args.LineNumber, args.ColumnNumber, args.Code, args?.Message));
  }

  public void Shutdown() { }
}