using EasyDotnet.BuildServer.Logging;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public sealed record BuildFreshnessResult(bool IsUpToDate, string? Reason);

public sealed class BuildFreshnessChecker(InputPredictor predictor, Logger logger)
{
  public BuildFreshnessResult Check(string projectPath, string? configuration, string targetFramework)
  {
    var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["Configuration"] = configuration ?? "Debug",
      ["TargetFramework"] = targetFramework,
      ["DesignTimeBuild"] = "true",
      ["BuildProjectReferences"] = "false",
      ["SkipCompilerExecution"] = "true",
      ["ProvideCommandLineArgs"] = "false",
      ["ResolveAssemblyReferencesDesignTime"] = "true",
      ["GeneratePackageOnBuild"] = "false",
    };

    using var pc = new ProjectCollection(globalProperties);

    Project? project = null;
    try
    {
      project = pc.LoadProject(projectPath, globalProperties, toolsVersion: null);

      var disable = project.GetPropertyValue("DisableFastUpToDateCheck");
      if (string.Equals(disable, "true", StringComparison.OrdinalIgnoreCase))
      {
        logger.LogInformation("FUTD disabled by project: {Project} ({Tfm})", projectPath, targetFramework);
        return new BuildFreshnessResult(false, "DisableFastUpToDateCheck=true");
      }

      var enable = project.GetPropertyValue("EnableFastUpToDateCheck");
      if (!string.Equals(enable, "true", StringComparison.OrdinalIgnoreCase))
      {
        return new BuildFreshnessResult(false, "EnableFastUpToDateCheck=true");
      }

      var targetPath = project.GetPropertyValue("TargetPath");
      if (string.IsNullOrEmpty(targetPath))
      {
        return new BuildFreshnessResult(false, "Project has no TargetPath");
      }
      if (!File.Exists(targetPath))
      {
        return new BuildFreshnessResult(false, $"TargetPath missing: {targetPath}");
      }
      var outputTicks = File.GetLastWriteTimeUtc(targetPath).Ticks;

      var instance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
      var prediction = predictor.Predict(instance);

      long maxInputTicks = long.MinValue;
      string? newestInput = null;
      foreach (var i in prediction.InputFiles)
      {
        if (!File.Exists(i)) continue;
        var t = File.GetLastWriteTimeUtc(i).Ticks;
        if (t > maxInputTicks) { maxInputTicks = t; newestInput = i; }
      }
      foreach (var dir in prediction.InputDirectories)
      {
        if (!Directory.Exists(dir)) continue;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
          var t = File.GetLastWriteTimeUtc(f).Ticks;
          if (t > maxInputTicks) { maxInputTicks = t; newestInput = f; }
        }
      }

      if (maxInputTicks > outputTicks)
      {
        return new BuildFreshnessResult(false, $"Input newer than TargetPath: {newestInput}");
      }

      logger.LogInformation("FUTD up-to-date: {Project} ({Tfm}) TargetPath={TargetPath}", projectPath, targetFramework, targetPath);
      return new BuildFreshnessResult(true, null);
    }
    finally
    {
      if (project != null)
      {
        try { pc.UnloadProject(project); } catch { }
      }
    }
  }

}