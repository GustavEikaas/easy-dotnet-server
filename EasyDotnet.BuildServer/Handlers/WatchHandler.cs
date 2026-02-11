using System.Text.Json;
using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class WatchHandler(SdkInstallation instance)
{
  private static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

  [JsonRpcMethod("project/get-watchlist", UseSingleObjectParameterDeserialization = true)]
  public GetWatchListResponse GetWatchList(GetWatchListRequest request)
  {
    var watchTargetsFile = GetDotnetWatchTargets();
    var tempFile = Path.GetTempFileName();

    try
    {
      var globalProperties = new Dictionary<string, string>
      {
        ["_DotNetWatchListFile"] = tempFile,
        ["DotNetWatchBuild"] = "true",
        ["DesignTimeBuild"] = "true",
        ["CustomAfterMicrosoftCommonTargets"] = watchTargetsFile,
        ["CustomAfterMicrosoftCommonCrossTargetingTargets"] = watchTargetsFile,
        ["Configuration"] = request.Configuration
      };

      using var projectCollection = new ProjectCollection(globalProperties);
      var project = projectCollection.LoadProject(request.ProjectPath);

      var errors = new List<string>();
      var errorLogger = new ErrorLogger(errors);

      if (!project.Build("GenerateWatchList", [errorLogger]))
      {
        var errorMessage = errors.Count != 0
          ? $"Failed to generate watch list: {string.Join("; ", errors)}"
          : "Failed to generate watch list";
        throw new InvalidOperationException(errorMessage);
      }

      var json = File.ReadAllText(tempFile);
      var watchList = JsonSerializer.Deserialize<GetWatchListResponse>(json, JsonSerializerOptions);

      if (watchList?.Projects == null || watchList.Projects.Count == 0)
      {
        throw new InvalidOperationException("No projects found in watch list");
      }

      return watchList;
    }
    finally
    {
      if (File.Exists(tempFile))
      {
        File.Delete(tempFile);
      }
    }
  }

  private class ErrorLogger(List<string> errors) : ILogger
  {
    private readonly List<string> _errors = errors;

    public LoggerVerbosity Verbosity { get; set; }
    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource) => eventSource.ErrorRaised += (sender, e) => _errors.Add(e?.Message ?? "");

    public void Shutdown() { }
  }

  private string GetDotnetWatchTargets()
  {
    var version = instance.Version;
    var path = instance.MSBuildPath;
    var moniker = instance.Moniker;
    return Path.Combine(path, "DotnetTools", "dotnet-watch", version.ToString(), "tools", moniker, "any", "DotnetWatch.targets");
  }
}
