using Microsoft.Build.Execution;
using Microsoft.Build.Prediction;

namespace EasyDotnet.BuildServer.MsBuildProject.Cache;

public sealed class InputPredictor
{
  private readonly ProjectPredictionExecutor _executor =
      new(ProjectPredictors.AllProjectPredictors);

  public PredictionResult Predict(ProjectInstance instance)
  {
    var predictions = _executor.PredictInputsAndOutputs(instance);

    var baseDir = instance.Directory;

    var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in instance.Items)
    {
      if (!string.Equals(item.GetMetadataValue("ExcludedFromBuild"), "true", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      var p = item.GetMetadataValue("FullPath");
      if (!string.IsNullOrEmpty(p))
      {
        excluded.Add(Path.GetFullPath(p));
      }
    }

    var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in predictions.InputFiles)
    {
      var n = Normalize(item.Path, baseDir);
      if (excluded.Contains(n)) continue;
      files.Add(n);
    }

    if (!string.IsNullOrEmpty(instance.FullPath))
    {
      files.Add(Normalize(instance.FullPath, baseDir));
    }

    foreach (var import in instance.ImportPaths)
    {
      if (!string.IsNullOrEmpty(import))
      {
        files.Add(Normalize(import, baseDir));
      }
    }

    var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in predictions.InputDirectories)
    {
      dirs.Add(Normalize(item.Path, baseDir));
    }

    if (!string.IsNullOrEmpty(baseDir))
    {
      dirs.Add(Path.GetFullPath(baseDir));
    }

    var outFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in predictions.OutputFiles)
    {
      outFiles.Add(Normalize(item.Path, baseDir));
    }
    var outDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in predictions.OutputDirectories)
    {
      outDirs.Add(Normalize(item.Path, baseDir));
    }

    return new PredictionResult([.. files], [.. dirs], [.. outFiles], [.. outDirs]);
  }

  private static string Normalize(string path, string baseDir)
  {
    var p = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    var full = Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p);
    return Path.GetFullPath(full);
  }

  public sealed record PredictionResult(
      List<string> InputFiles,
      List<string> InputDirectories,
      List<string> OutputFiles,
      List<string> OutputDirectories);
}