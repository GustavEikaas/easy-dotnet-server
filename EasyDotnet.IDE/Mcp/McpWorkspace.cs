using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using EasyDotnet.IDE.TestRunner.Service;

namespace EasyDotnet.IDE.Mcp;

/// <summary>Resolves agent-supplied paths against the workspace and prepares the testrunner.</summary>
public sealed class McpWorkspace(IClientService clientService, TestRunnerService testRunner, NodeRegistry registry)
{
  private int _syncVersion;

  public string RootDir => clientService.RequireRootDir();

  public string? SolutionFile => clientService.ProjectInfo?.SolutionFile;

  public string ResolvePath(string path) => Path.GetFullPath(path, RootDir);

  public string ResolveBuildTarget(string? target)
  {
    if (string.IsNullOrWhiteSpace(target))
    {
      return SolutionFile ?? SingleProjectInRoot();
    }

    var full = ResolvePath(target);
    if (!File.Exists(full))
      throw new ArgumentException($"'{full}' does not exist.");
    if (!DotnetFileTypes.IsAnySolutionFile(full) && !DotnetFileTypes.IsAnyProjectFile(full))
      throw new ArgumentException($"'{full}' is not a solution or project file.");
    return full;
  }

  /// <summary>Discovers the solution's tests the first time; later calls return immediately.</summary>
  public async Task EnsureTestRunnerAsync(CancellationToken ct)
  {
    var solution = SolutionFile ?? throw new InvalidOperationException(
        "The test tools need a solution file. Start the server with --solution <path>, or from a directory that contains exactly one .sln or .slnx.");
    await testRunner.InitializeAsync(solution, ct);
  }

  /// <summary>
  /// Brings the testrunner's view of <paramref name="file"/> up to date with the file on disk.
  /// Line numbers and not-yet-built tests in known classes come from parsing the source; a file
  /// the testrunner has never seen makes its project build and rediscover.
  /// </summary>
  public async Task SyncTestFileAsync(string file, CancellationToken ct)
  {
    var path = ResolvePath(file);
    if (!File.Exists(path))
      throw new ArgumentException($"'{path}' does not exist.");

    await testRunner.SyncFileAsync(new SyncFileRequest(path, await File.ReadAllTextAsync(path, ct), Interlocked.Increment(ref _syncVersion)));

    if (registry.GetNodesForFile(path).Any()) return;

    var project = OwningProject(path);
    if (project is null) return;

    await testRunner.InvalidateAsync(project.Id, ct);
    if (registry.GetLastStatusKind(project.Id) is TestNodeStatusKind.BuildFailed)
    {
      throw new InvalidOperationException(
          $"{project.DisplayName} does not build, so the tests in '{path}' cannot be discovered. Call build on it to see the errors.");
    }
  }

  private TestNode? OwningProject(string path) =>
      registry.GetAll()
          .Where(n => n.Type is NodeType.Project && n.FilePath is not null)
          .Where(n => path.StartsWith(Path.GetDirectoryName(n.FilePath)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(n => n.FilePath!.Length)
          .FirstOrDefault();

  private string SingleProjectInRoot()
  {
    var projects = Directory.EnumerateFiles(RootDir, "*.*proj")
        .Where(DotnetFileTypes.IsAnyProjectFile)
        .ToList();

    return projects.Count == 1
        ? projects[0]
        : throw new ArgumentException(projects.Count == 0
            ? $"No solution or project file found in '{RootDir}'. Pass target explicitly."
            : $"Several projects in '{RootDir}' and no solution. Pass target explicitly: {string.Join(", ", projects.Select(Path.GetFileName))}");
  }
}