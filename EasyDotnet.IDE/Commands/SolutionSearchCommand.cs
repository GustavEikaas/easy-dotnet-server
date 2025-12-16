using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.MsBuild;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class SolutionSearchCommand : AsyncCommand<SolutionSearchCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandOption("-d|--depth")]
    public int? Depth { get; set; }
  }

  private static bool IsNotIgnored(string dirPath)
  {
    var dirName = Path.GetFileName(dirPath);
    return dirName is not (".git" or "bin" or "obj" or "node_modules" or ".vs" or ".idea");
  }

  private static bool IsSolutionFile(string filePath) =>
      filePath.EndsWith(FileTypes.SolutionExtension, StringComparison.OrdinalIgnoreCase) ||
      filePath.EndsWith(FileTypes.SolutionXExtension, StringComparison.OrdinalIgnoreCase);

  public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var solutions = FindSolutionsRecursive(Directory.GetCurrentDirectory(), 0, settings.Depth);

    var json = JsonSerializer.Serialize(solutions);
    Console.WriteLine(json);

    return Task.FromResult(0);
  }

  private static IEnumerable<string> FindSolutionsRecursive(string currentDir, int currentDepth, int? maxDepth)
  {
    if (maxDepth.HasValue && currentDepth > maxDepth.Value)
      return [];

    var files = SafeEnumerateFiles(currentDir).Where(IsSolutionFile);

    var subDirFiles = SafeEnumerateDirectories(currentDir)
        .Where(IsNotIgnored)
        .SelectMany(dir => FindSolutionsRecursive(dir, currentDepth + 1, maxDepth));

    return files.Concat(subDirFiles);
  }

  private static IEnumerable<string> SafeEnumerateFiles(string path)
  {
    try { return Directory.EnumerateFiles(path); }
    catch { return []; }
  }

  private static IEnumerable<string> SafeEnumerateDirectories(string path)
  {
    try { return Directory.EnumerateDirectories(path); }
    catch { return []; }
  }
}