using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class SolutionSearchCommand : AsyncCommand<SolutionSearchCommand.Settings>
{
  public sealed class Settings : CommandSettings;

  private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs", ".idea"
    };

  public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var cwd = Directory.GetCurrentDirectory();
    var solutions = new List<string>();

    SearchRecursive(cwd, 0, 5, solutions);

    var json = JsonSerializer.Serialize(solutions);
    Console.WriteLine(json);

    return Task.FromResult(0);
  }

  private void SearchRecursive(string currentDir, int currentDepth, int maxDepth, List<string> results)
  {
    if (currentDepth > maxDepth) return;

    try
    {
      foreach (var file in Directory.EnumerateFiles(currentDir))
      {
        if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
          results.Add(file);
        }
      }

      foreach (var dir in Directory.EnumerateDirectories(currentDir))
      {
        var dirName = Path.GetFileName(dir);

        if (IgnoredFolders.Contains(dirName)) continue;

        SearchRecursive(dir, currentDepth + 1, maxDepth, results);
      }
    }
    catch (UnauthorizedAccessException) { }
    catch (Exception) { }
  }
}