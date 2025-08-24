using System.Diagnostics;
using EasyDotnet.MsBuild.Contracts;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using StreamJsonRpc;

namespace EasyDotnet.MsBuildSdk.Controllers;

public class MsbuildController(SdkInstallation[] monikers, DotnetProjectCache cache)
{

  [JsonRpcMethod("msbuild/build")]
  public MsBuild.Contracts.BuildResult RequestBuild(string targetPath, string configuration)
  {
    var properties = new Dictionary<string, string?> { { "Configuration", configuration } };

    using var pc = new ProjectCollection();
    var buildRequest = new BuildRequestData(targetPath, properties, null, ["Restore", "Build"], null);
    var logger = new InMemoryLogger();

    var parameters = new BuildParameters(pc) { Loggers = [logger] };

    var result = BuildManager.DefaultBuildManager.Build(parameters, buildRequest);

    return new MsBuild.Contracts.BuildResult(Success: result.OverallResult == BuildResultCode.Success, logger.Errors, logger.Warnings);
  }

  [JsonRpcMethod("msbuild/sdk-installations")]
  public SdkInstallation[] QuerySdkInstallations() => monikers;

  public class DotnetRecord
  {
    public string Path { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string ResponseTime { get; set; } = "";
    public bool IsCached { get; set; }
    public string CacheDir { get; set; } = "";
  }

  [JsonRpcMethod("msbuild/project-properties")]
  public DotnetRecord ProjectProperties(string targetPath, string configuration)
  {
    var sw = Stopwatch.StartNew();
    var is_cached = true;

    var record = cache.GetOrCreate(targetPath, (path, cachedir) =>
           {
             is_cached = false;
             var properties = new Dictionary<string, string?>
              {
                  { "Configuration", configuration }
              };

             using var pc = new ProjectCollection();
             var project = pc.LoadProject(targetPath, properties, toolsVersion: null);

             return new CachedRecord
             {
               Path = project.FullPath,
               TargetPath = project.GetPropertyValue("TargetPath"),
               OutputPath = project.GetPropertyValue("OutputPath"),
               Imports = [.. project.Imports.Select(x => x.ImportedProject.FullPath).Where(x => File.Exists(x))],
               CacheDir = cachedir,
               LastVerified = DateTime.UtcNow,
               CreatedAt = DateTime.UtcNow
             };
           });

    sw.Stop();

    var elapsedSeconds = sw.Elapsed.TotalSeconds;
    record.ResponseTime = $"{elapsedSeconds} seconds";
    return new DotnetRecord()
    {
      Path = record.Path,
      TargetPath = record.TargetPath,
      OutputPath = record.OutputPath,
      ResponseTime = record.ResponseTime,
      IsCached = is_cached,
      CacheDir = record.CacheDir
    };
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