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

      var globallyEnabled = string.Equals(
          Environment.GetEnvironmentVariable("EASYDOTNET_FUTD_ENABLED"),
          "true",
          StringComparison.OrdinalIgnoreCase);

      if (!globallyEnabled)
      {
        var enable = project.GetPropertyValue("EnableFastUpToDateCheck");
        if (!string.Equals(enable, "true", StringComparison.OrdinalIgnoreCase))
        {
          return new BuildFreshnessResult(false, "EnableFastUpToDateCheck!=true");
        }
      }

      var targetPath = project.GetPropertyValue("TargetPath");
      if (string.IsNullOrEmpty(targetPath))
      {
        return new BuildFreshnessResult(false, "Project has no TargetPath");
      }

      var instance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
      var prediction = predictor.Predict(instance);

      var extraInputs = CollectItems(instance, "UpToDateCheckInput");
      var extraOutputs = CollectItems(instance, "UpToDateCheckOutput");
      var builtItems = CollectBuiltItems(instance);

      var minOutputTicks = long.MaxValue;
      string? oldestOutput = null;
      string? missingOutput = null;

      void ConsiderOutput(string path)
      {
        if (!File.Exists(path))
        {
          missingOutput ??= path;
          return;
        }
        var t = File.GetLastWriteTimeUtc(path).Ticks;
        if (t < minOutputTicks) { minOutputTicks = t; oldestOutput = path; }
      }

      ConsiderOutput(targetPath);
      foreach (var o in extraOutputs) ConsiderOutput(o);
      foreach (var (built, original) in builtItems)
      {
        if (original == null) ConsiderOutput(built);
      }

      if (missingOutput != null)
      {
        return new BuildFreshnessResult(false, $"Output missing: {missingOutput}");
      }

      var maxInputTicks = long.MinValue;
      string? newestInput = null;

      void ConsiderInput(string path)
      {
        if (!File.Exists(path)) return;
        var t = File.GetLastWriteTimeUtc(path).Ticks;
        if (t > maxInputTicks) { maxInputTicks = t; newestInput = path; }
      }

      foreach (var i in prediction.InputFiles) ConsiderInput(i);
      foreach (var i in extraInputs) ConsiderInput(i);
      foreach (var dir in prediction.InputDirectories)
      {
        if (!Directory.Exists(dir)) continue;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
          var t = File.GetLastWriteTimeUtc(f).Ticks;
          if (t > maxInputTicks) { maxInputTicks = t; newestInput = f; }
        }
      }

      if (maxInputTicks > minOutputTicks)
      {
        return new BuildFreshnessResult(false, $"Input '{newestInput}' newer than output '{oldestOutput}'");
      }

      foreach (var (built, original) in builtItems)
      {
        if (original == null) continue;
        if (!File.Exists(original))
        {
          return new BuildFreshnessResult(false, $"UpToDateCheckBuilt Original missing: {original}");
        }
        if (!File.Exists(built))
        {
          return new BuildFreshnessResult(false, $"UpToDateCheckBuilt destination missing: {built}");
        }
        var origTicks = File.GetLastWriteTimeUtc(original).Ticks;
        var builtTicks = File.GetLastWriteTimeUtc(built).Ticks;
        if (origTicks > builtTicks)
        {
          return new BuildFreshnessResult(false, $"Copy source '{original}' newer than '{built}'");
        }
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

  private static List<string> CollectItems(ProjectInstance instance, string itemType)
  {
    var result = new List<string>();
    foreach (var item in instance.GetItems(itemType))
    {
      if (string.Equals(item.GetMetadataValue("ExcludedFromBuild"), "true", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      var path = item.GetMetadataValue("FullPath");
      if (!string.IsNullOrEmpty(path))
      {
        result.Add(path);
      }
    }
    return result;
  }

  private static List<(string Built, string? Original)> CollectBuiltItems(ProjectInstance instance)
  {
    var result = new List<(string, string?)>();
    foreach (var item in instance.GetItems("UpToDateCheckBuilt"))
    {
      if (string.Equals(item.GetMetadataValue("ExcludedFromBuild"), "true", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      var built = item.GetMetadataValue("FullPath");
      if (string.IsNullOrEmpty(built)) continue;
      var original = item.GetMetadataValue("Original");
      string? originalFull = null;
      if (!string.IsNullOrEmpty(original))
      {
        originalFull = Path.IsPathRooted(original)
            ? original
            : Path.GetFullPath(Path.Combine(instance.Directory, original));
      }
      result.Add((built, originalFull));
    }
    return result;
  }
}