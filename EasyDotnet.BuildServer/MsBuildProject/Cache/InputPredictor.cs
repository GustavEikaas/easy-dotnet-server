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

    var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in predictions.InputFiles)
    {
      files.Add(Normalize(item.Path, baseDir));
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

    return new PredictionResult(files.ToList(), dirs.ToList(), outFiles.ToList(), outDirs.ToList());
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