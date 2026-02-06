using EasyDotnet.BuildServer.Models;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class BuildService(IIdeLogger ideLogger)
{
  private readonly BuildManager _buildManager = BuildManager.DefaultBuildManager;

  [JsonRpcMethod("build", UseSingleObjectParameterDeserialization = true)]
  public BuildRpcResult Build(BuildRequest request)
  {
    var props = MergeProperties(request.Properties, request.Configuration);
    return ExecuteTarget(request.ProjectFile, ["Build"], props);
  }

  [JsonRpcMethod("restore", UseSingleObjectParameterDeserialization = true)]
  public BuildRpcResult Restore(RestoreRequest request)
  {
    var props = request.Properties ?? [];
    return ExecuteTarget(request.ProjectFile, ["Restore"], props);
  }

  [JsonRpcMethod("clean", UseSingleObjectParameterDeserialization = true)]
  public BuildRpcResult Clean(CleanRequest request)
  {
    var props = MergeProperties(request.Properties, request.Configuration);
    return ExecuteTarget(request.ProjectFile, ["Clean"], props);
  }

  [JsonRpcMethod("publish", UseSingleObjectParameterDeserialization = true)]
  public BuildRpcResult Publish(PublishRequest request)
  {
    var props = MergeProperties(request.Properties, request.Configuration);
    return ExecuteTarget(request.ProjectFile, ["Publish"], props);
  }

  private BuildRpcResult ExecuteTarget(string projectFile, string[] targets, Dictionary<string, string> globalProperties)
  {
    // 1. Log INPUTS (Debug Level)
    _ = ideLogger.LogAsync($"[Start] Project: {projectFile}", 0);
    _ = ideLogger.LogAsync($"[Start] Targets: {string.Join(", ", targets)}", 3);

    foreach (var kvp in globalProperties)
    {
      _ = ideLogger.LogAsync($"[Start] Prop: {kvp.Key}={kvp.Value}", 3);
    }

    // 2. Setup Logger
    var logger = new InMemoryLogger();

    // 3. Setup Parameters
    var parameters = new BuildParameters(ProjectCollection.GlobalProjectCollection)
    {
      Loggers = [logger],
      MaxNodeCount = Environment.ProcessorCount,
      DetailedSummary = false,
      EnableNodeReuse = true,

      // CRITICAL DEBUGGING: 
      // If the tools version is wrong, MSBuild fails silently. 
      // "Current" is usually safe for modern .NET (6/7/8+).
      DefaultToolsVersion = "Current"
    };

    var requestData = new BuildRequestData(
        projectFile,
        globalProperties,
        toolsVersion: null,
        targets,
        hostServices: null
    );

    try
    {
      _ = ideLogger.LogAsync("[Step] Submitting to BuildManager...", 3);

      var result = _buildManager.Build(parameters, requestData);

      _ = ideLogger.LogAsync($"[Step] BuildManager returned: {result.OverallResult}", 0);

      // 4. DIAGNOSE SILENT FAILURES
      if (result.OverallResult != BuildResultCode.Success && logger.Errors.Count == 0)
      {
        _ = ideLogger.LogAsync("[Alert] Build Failed with 0 Errors captured!", 2);

        // Check if the engine itself threw an exception (e.g. invalid arguments)
        if (result.Exception != null)
        {
          _ = ideLogger.LogAsync($"[Exception] Engine Exception: {result.Exception.Message}", 2);
          _ = ideLogger.LogAsync($"[Exception] Stack: {result.Exception.StackTrace}", 3);
        }
      }
      else if (result.OverallResult == BuildResultCode.Success)
      {
        _ = ideLogger.LogAsync($"[Success] Build finished with {logger.Errors.Count} errors and {logger.Warnings.Count} warnings.", 0);
      }

      return new BuildRpcResult(
          result.OverallResult == BuildResultCode.Success,
          logger.Errors,
          logger.Warnings,
          result.OverallResult.ToString()
      );
    }
    catch (Exception ex)
    {
      // 5. CATCH CRASHES
      _ = ideLogger.LogAsync($"[Crash] ExecuteTarget crashed: {ex.Message}", 2);

      return new BuildRpcResult(false,
          [new("Error", "", 0, 0, "CRITICAL", ex.Message, projectFile)],
          [],
          "Crashed"
      );
    }
  }

  private static Dictionary<string, string> MergeProperties(Dictionary<string, string>? incoming, string? configuration)
  {
    var props = incoming != null
        ? new Dictionary<string, string>(incoming)
        : [];

    if (!string.IsNullOrEmpty(configuration) && !props.ContainsKey("Configuration"))
    {
      props["Configuration"] = configuration;
    }

    return props;
  }
}