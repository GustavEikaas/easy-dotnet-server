using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public sealed record BuildFreshnessResult(bool IsUpToDate, string? Reason);

public sealed class BuildFreshnessChecker(InputPredictor predictor)
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
        return new BuildFreshnessResult(false, "DisableFastUpToDateCheck=true");
      }

      var instance = project.CreateProjectInstance(ProjectInstanceSettings.ImmutableWithFastItemLookup);
      var prediction = predictor.Predict(instance);

      var outputs = new List<string>(prediction.OutputFiles);
      foreach (var d in prediction.OutputDirectories)
      {
        if (!Directory.Exists(d))
        {
          return new BuildFreshnessResult(false, $"Output directory missing: {d}");
        }
        outputs.AddRange(Directory.EnumerateFiles(d));
      }

      if (outputs.Count == 0)
      {
        return new BuildFreshnessResult(false, "No predicted outputs");
      }

      long minOutputTicks = long.MaxValue;
      foreach (var o in outputs)
      {
        if (!File.Exists(o))
        {
          return new BuildFreshnessResult(false, $"Output missing: {o}");
        }
        var t = File.GetLastWriteTimeUtc(o).Ticks;
        if (t < minOutputTicks) minOutputTicks = t;
      }

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

      if (maxInputTicks > minOutputTicks)
      {
        return new BuildFreshnessResult(false, $"Input newer than output: {newestInput}");
      }

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
