using EasyDotnet.MsBuild.Contracts;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using StreamJsonRpc;

namespace EasyDotnet.MsBuildSdk.Controllers;

public class MsbuildController(SdkInstallation[] monikers)
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