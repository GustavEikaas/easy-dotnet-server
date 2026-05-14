using System.Collections.Concurrent;
using EasyDotnet.BuildServer.Logging;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;

namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public sealed record BuildFreshnessResult(bool IsUpToDate, string? Reason);

public sealed class BuildFreshnessChecker(InputPredictor predictor, Logger logger)
{
  private readonly ConcurrentDictionary<BuildFreshnessKey, SuccessfulBuildState> _successfulBuilds = new();

  public BuildFreshnessResult Check(string projectPath, string? configuration, string? platform, string targetFramework)
  {
    if (!IsSupportedRuntime)
    {
      return new BuildFreshnessResult(false, "FUTD is only enabled on the net8 build server");
    }

    var globalProperties = CreateGlobalProperties(configuration, platform, targetFramework);

    var key = BuildFreshnessKey.Create(projectPath, globalProperties["Configuration"], platform, targetFramework);

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

      var instance = project.CreateProjectInstance();
      var prediction = predictor.Predict(instance);
      var designTimeData = CollectDesignTimeData(instance);
      var coverageMissReason = GetCoverageMissReason(designTimeData);
      if (coverageMissReason is not null)
      {
        return new BuildFreshnessResult(false, coverageMissReason);
      }

      var extraInputs = designTimeData.Inputs;
      var extraOutputs = designTimeData.Outputs;
      var builtItems = designTimeData.BuiltItems;
      var currentInputSet = CreateInputSet(prediction.InputFiles, extraInputs);

      if (!_successfulBuilds.TryGetValue(key, out var successfulBuild))
      {
        return new BuildFreshnessResult(false, "FirstRun");
      }

      if (!currentInputSet.SetEquals(successfulBuild.InputFiles))
      {
        return new BuildFreshnessResult(false, "Project input item set changed since last successful build");
      }

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
        if (IsSamePath(path, project.FullPath))
        {
          var projectTicks = File.GetLastWriteTimeUtc(project.FullPath).Ticks;
          if (projectTicks > maxInputTicks) { maxInputTicks = projectTicks; newestInput = project.FullPath; }
          return;
        }

        if (!File.Exists(path))
        {
          newestInput ??= path;
          maxInputTicks = long.MaxValue;
          return;
        }
        var t = File.GetLastWriteTimeUtc(path).Ticks;
        if (t > maxInputTicks) { maxInputTicks = t; newestInput = path; }
      }

      foreach (var i in currentInputSet) ConsiderInput(i);
      foreach (var dir in prediction.InputDirectories)
      {
        if (!Directory.Exists(dir)) continue;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
          var t = File.GetLastWriteTimeUtc(f).Ticks;
          if (t > maxInputTicks) { maxInputTicks = t; newestInput = f; }
        }
      }

      if (maxInputTicks == long.MaxValue)
      {
        return new BuildFreshnessResult(false, $"Input missing: {newestInput}");
      }

      if (maxInputTicks > successfulBuild.StartedAtUtcTicks)
      {
        return new BuildFreshnessResult(false, $"Input '{newestInput}' modified since last successful build started");
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

      foreach (var copyItem in designTimeData.CopyItems)
      {
        if (string.Equals(copyItem.SourcePath, copyItem.DestinationPath, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        if (!File.Exists(copyItem.SourcePath))
        {
          return new BuildFreshnessResult(false, $"CopyToOutputDirectory source missing: {copyItem.SourcePath}");
        }

        if (!File.Exists(copyItem.DestinationPath))
        {
          return new BuildFreshnessResult(false, $"CopyToOutputDirectory destination missing: {copyItem.DestinationPath}");
        }

        var sourceTime = File.GetLastWriteTimeUtc(copyItem.SourcePath);
        var destinationTime = File.GetLastWriteTimeUtc(copyItem.DestinationPath);

        if (string.Equals(copyItem.CopyType, "Always", StringComparison.OrdinalIgnoreCase)
            || string.Equals(copyItem.CopyType, "IfDifferent", StringComparison.OrdinalIgnoreCase))
        {
          var sourceLength = new FileInfo(copyItem.SourcePath).Length;
          var destinationLength = new FileInfo(copyItem.DestinationPath).Length;
          if (sourceTime != destinationTime || sourceLength != destinationLength)
          {
            return new BuildFreshnessResult(false, $"CopyToOutputDirectory {copyItem.CopyType} item differs: {copyItem.SourcePath}");
          }
        }
        else if (sourceTime > destinationTime)
        {
          return new BuildFreshnessResult(false, $"CopyToOutputDirectory source '{copyItem.SourcePath}' newer than '{copyItem.DestinationPath}'");
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

  public void RecordSuccessfulBuild(string projectPath, string? configuration, string? platform, string targetFramework, DateTime startedAtUtc)
  {
    if (!IsSupportedRuntime)
    {
      return;
    }

    var globalProperties = CreateGlobalProperties(configuration, platform, targetFramework);
    var key = BuildFreshnessKey.Create(projectPath, globalProperties["Configuration"], platform, targetFramework);

    using var pc = new ProjectCollection(globalProperties);

    Project? project = null;
    try
    {
      project = pc.LoadProject(projectPath, globalProperties, toolsVersion: null);
      var instance = project.CreateProjectInstance();
      var prediction = predictor.Predict(instance);
      var designTimeData = CollectDesignTimeData(instance);
      var coverageMissReason = GetCoverageMissReason(designTimeData);
      if (coverageMissReason is not null)
      {
        logger.LogInformation("FUTD not recording successful build state for {Project} ({Tfm}): {Reason}", projectPath, targetFramework, coverageMissReason);
        return;
      }

      _successfulBuilds[key] = new SuccessfulBuildState(
          CreateInputSet(prediction.InputFiles, designTimeData.Inputs),
          startedAtUtc.Ticks);
    }
    catch (Exception ex)
    {
      logger.LogWarning("FUTD failed to record successful build state for {Project} ({Tfm}): {Message}", projectPath, targetFramework, ex.Message);
    }
    finally
    {
      if (project != null)
      {
        try { pc.UnloadProject(project); } catch { }
      }
    }
  }

  private static Dictionary<string, string> CreateGlobalProperties(string? configuration, string? platform, string targetFramework)
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

    if (!string.IsNullOrWhiteSpace(platform))
    {
      globalProperties["Platform"] = platform!;
    }

    return globalProperties;
  }

  private static bool IsSupportedRuntime =>
#if NET8_0_OR_GREATER
      true;
#else
      false;
#endif

  private static HashSet<string> CreateInputSet(IEnumerable<string> inputFiles, IEnumerable<string> extraInputs)
  {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var input in inputFiles.Concat(extraInputs))
    {
      if (!string.IsNullOrWhiteSpace(input))
      {
        result.Add(Path.GetFullPath(input));
      }
    }

    return result;
  }

  private static string? GetCoverageMissReason(DesignTimeData data)
  {
    if (!data.HasBuiltOutputCollection)
    {
      return "CollectUpToDateCheckBuiltDesignTime unavailable";
    }

    if (data.HasProjectReferences && !data.HasResolvedCompilationReferenceCoverage)
    {
      return "ProjectReference present but CollectResolvedCompilationReferencesDesignTime unavailable";
    }

    return null;
  }

  private static DesignTimeData CollectDesignTimeData(ProjectInstance instance)
  {
    var hasBuiltOutputCollection = instance.Targets.ContainsKey("CollectUpToDateCheckBuiltDesignTime");
    var hasProjectReferences = instance.GetItems("ProjectReference").Any(IsIncludedInBuild);
    var hasResolvedCompilationReferenceCoverage = !hasProjectReferences;

    var inputs = new List<string>();
    var outputs = new List<string>();
    var builtItems = new List<(string Built, string? Original)>();
    var copyItems = new List<CopyToOutputItem>();
    var outputDirectory = GetOutputDirectory(instance);

    foreach (var item in instance.GetItems("Analyzer"))
    {
      AddItemPath(inputs, item, "ResolvedPath", instance.Directory);
      AddItemPath(inputs, item, null, instance.Directory);
    }

    if (TryBuildTarget(instance, "CollectResolvedCompilationReferencesDesignTime", out var resolvedReferences))
    {
      hasResolvedCompilationReferenceCoverage = true;
      foreach (var item in resolvedReferences)
      {
        AddItemPath(inputs, item, "ResolvedPath", instance.Directory);
        AddItemPath(inputs, item, "OriginalPath", instance.Directory);
        AddItemPath(inputs, item, "CopyUpToDateMarker", instance.Directory);
      }
    }

    foreach (var item in BuildTargetAndDependsOnTargets(instance, "CollectUpToDateCheckInputDesignTime", "CollectUpToDateCheckInputDesignTimeDependsOn"))
    {
      AddItemPath(inputs, item, null, instance.Directory);
    }
    foreach (var item in instance.GetItems("UpToDateCheckInput"))
    {
      AddItemPath(inputs, item, null, instance.Directory);
    }

    foreach (var item in BuildTargetAndDependsOnTargets(instance, "CollectUpToDateCheckOutputDesignTime", "CollectUpToDateCheckOutputDesignTimeDependsOn"))
    {
      AddItemPath(outputs, item, null, instance.Directory);
    }
    foreach (var item in instance.GetItems("UpToDateCheckOutput"))
    {
      AddItemPath(outputs, item, null, instance.Directory);
    }

    foreach (var item in BuildTargetAndDependsOnTargets(instance, "CollectUpToDateCheckBuiltDesignTime", "CollectUpToDateCheckBuiltDesignTimeDependsOn"))
    {
      var built = GetItemPath(item, null, instance.Directory);
      if (built is null) continue;

      var original = GetItemPath(item, "Original", instance.Directory);
      builtItems.Add((built, original));
    }
    foreach (var item in instance.GetItems("UpToDateCheckBuilt"))
    {
      var built = GetItemPath(item, null, instance.Directory);
      if (built is null) continue;

      var original = GetItemPath(item, "Original", instance.Directory);
      builtItems.Add((built, original));
    }

    foreach (var item in BuildTargetAndDependsOnTargets(instance, "CollectCopyToOutputDirectoryItemDesignTime", null))
    {
      AddCopyToOutputItem(copyItems, item, instance.Directory, outputDirectory, preserveSourceRelativePath: false);
    }

    foreach (var item in instance.Items)
    {
      AddCopyToOutputItem(copyItems, item, instance.Directory, outputDirectory, preserveSourceRelativePath: true);
    }

    return new DesignTimeData(
        inputs,
        outputs,
        builtItems,
        copyItems,
        hasBuiltOutputCollection,
        hasResolvedCompilationReferenceCoverage,
        hasProjectReferences);
  }

  private static IEnumerable<ITaskItem> BuildTargetAndDependsOnTargets(ProjectInstance instance, string targetName, string? dependsOnPropertyName)
  {
    if (TryBuildTarget(instance, targetName, out var items))
    {
      return items;
    }

    if (dependsOnPropertyName is null)
    {
      return [];
    }

    var result = new List<ITaskItem>();
    foreach (var target in instance.GetPropertyValue(dependsOnPropertyName).Split([';'], StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()))
    {
      if (ShouldSkipFallbackDependsOnTarget(target)) continue;
      if (!TryBuildTarget(instance, target, out var targetItems)) continue;
      result.AddRange(targetItems);
    }

    return result;
  }

  private static bool TryBuildTarget(ProjectInstance instance, string targetName, out IReadOnlyCollection<ITaskItem> items)
  {
    items = [];
    if (!instance.Targets.ContainsKey(targetName))
    {
      return false;
    }

    if (!instance.Build([targetName], loggers: null, out var targetOutputs))
    {
      return false;
    }

    if (!targetOutputs.TryGetValue(targetName, out var result) || result.ResultCode == TargetResultCode.Failure)
    {
      return false;
    }

    items = result.Items;
    return true;
  }

  private static bool ShouldSkipFallbackDependsOnTarget(string targetName) =>
      string.Equals(targetName, "CompileDesignTime", StringComparison.OrdinalIgnoreCase)
      || string.Equals(targetName, "ResolveAssemblyReferences", StringComparison.OrdinalIgnoreCase);

  private static void AddItemPath(List<string> result, ITaskItem item, string? metadataName, string baseDirectory)
  {
    var path = GetItemPath(item, metadataName, baseDirectory);
    if (path is not null)
    {
      result.Add(path);
    }
  }

  private static string? GetItemPath(ITaskItem item, string? metadataName, string baseDirectory)
  {
    var value = metadataName is null
        ? item.ItemSpec
        : item.GetMetadata(metadataName);

    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
  }

  private static bool IsSamePath(string left, string right)
  {
    try
    {
      return string.Equals(
          Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
          Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
          StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
      return false;
    }
  }

  private static bool IsIncludedInBuild(ProjectItemInstance item) =>
      !string.Equals(item.GetMetadataValue("ExcludedFromBuild"), "true", StringComparison.OrdinalIgnoreCase);

  private static void AddCopyToOutputItem(List<CopyToOutputItem> copyItems, ITaskItem item, string baseDirectory, string outputDirectory, bool preserveSourceRelativePath)
  {
    var copyType = item.GetMetadata("CopyToOutputDirectory");
    if (string.IsNullOrWhiteSpace(copyType) || string.Equals(copyType, "Never", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    var source = GetItemPath(item, null, baseDirectory);
    if (source is null) return;

    var targetPath = item.GetMetadata("TargetPath");
    if (string.IsNullOrWhiteSpace(targetPath))
    {
      targetPath = item.GetMetadata("Link");
    }
    if (string.IsNullOrWhiteSpace(targetPath))
    {
      targetPath = preserveSourceRelativePath
          ? GetProjectRelativePathOrFileName(source, baseDirectory)
          : Path.GetFileName(source);
    }

    var destination = Path.GetFullPath(Path.Combine(outputDirectory, targetPath));
    copyItems.Add(new CopyToOutputItem(source, destination, copyType));
  }

  private static string GetProjectRelativePathOrFileName(string source, string baseDirectory)
  {
    if (TryGetRelativePath(baseDirectory, source, out var relativePath))
    {
      return relativePath;
    }

    return Path.GetFileName(source);
  }

  private static bool TryGetRelativePath(string baseDirectory, string path, out string relativePath)
  {
    relativePath = string.Empty;

    var baseUri = new Uri(EnsureTrailingDirectorySeparator(Path.GetFullPath(baseDirectory)));
    var pathUri = new Uri(Path.GetFullPath(path));

    if (!string.Equals(baseUri.Scheme, pathUri.Scheme, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var relativeUri = baseUri.MakeRelativeUri(pathUri);
    var value = Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
    if (value.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(value))
    {
      return false;
    }

    relativePath = value;
    return true;
  }

  private static string EnsureTrailingDirectorySeparator(string path)
  {
    if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
        || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
    {
      return path;
    }

    return path + Path.DirectorySeparatorChar;
  }

  private static string GetOutputDirectory(ProjectInstance instance)
  {
    var targetDir = instance.GetPropertyValue("TargetDir");
    if (!string.IsNullOrWhiteSpace(targetDir))
    {
      return Path.GetFullPath(targetDir);
    }

    var targetPath = instance.GetPropertyValue("TargetPath");
    var targetPathDirectory = Path.GetDirectoryName(targetPath);
    if (!string.IsNullOrWhiteSpace(targetPathDirectory))
    {
      return Path.GetFullPath(targetPathDirectory);
    }

    var outputPath = instance.GetPropertyValue("OutputPath");
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
      return Path.GetFullPath(Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(instance.Directory, outputPath));
    }

    var outDir = instance.GetPropertyValue("OutDir");
    if (!string.IsNullOrWhiteSpace(outDir))
    {
      return Path.GetFullPath(Path.IsPathRooted(outDir) ? outDir : Path.Combine(instance.Directory, outDir));
    }

    return instance.Directory;
  }

  private readonly record struct BuildFreshnessKey(
      string ProjectFullPath,
      string Configuration,
      string Platform,
      string TargetFramework)
  {
    public static BuildFreshnessKey Create(string projectPath, string configuration, string? platform, string targetFramework) =>
        new(
            Path.GetFullPath(projectPath),
            configuration,
            platform ?? string.Empty,
            targetFramework);
  }

  private sealed record SuccessfulBuildState(
      HashSet<string> InputFiles,
      long StartedAtUtcTicks);

  private sealed record DesignTimeData(
      List<string> Inputs,
      List<string> Outputs,
      List<(string Built, string? Original)> BuiltItems,
      List<CopyToOutputItem> CopyItems,
      bool HasBuiltOutputCollection,
      bool HasResolvedCompilationReferenceCoverage,
      bool HasProjectReferences);

  private sealed record CopyToOutputItem(
      string SourcePath,
      string DestinationPath,
      string CopyType);
}