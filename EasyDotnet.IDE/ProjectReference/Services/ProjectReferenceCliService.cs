using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;

namespace EasyDotnet.IDE.ProjectReference.Services;

public class ProjectReferenceCliService(
    IProcessQueue processQueue,
    WorkspaceBuildHostManager buildHostManager)
{
  public async Task<List<string>> GetProjectReferencesAsync(string projectPath, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(projectPath))
    {
      throw new ArgumentException("Project path must be provided", nameof(projectPath));
    }

    var fullProjectPath = Path.GetFullPath(projectPath);
    var (success, stdout, stderr) = await processQueue.RunProcessAsync(
        "dotnet",
        $"list \"{fullProjectPath}\" reference",
        new ProcessOptions(true),
        cancellationToken);

    if (!success)
    {
      throw new InvalidOperationException($"Failed to get project references: {stderr}");
    }

    var projectDir = Path.GetDirectoryName(fullProjectPath)!;

    return [.. stdout
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => DotnetFileTypes.IsAnyProjectFile(line))
        .Select(relativePath => Path.GetFullPath(Path.Combine(projectDir, relativePath)))];
  }

  public async Task<bool> AddProjectReferenceAsync(string projectPath, string targetPath, CancellationToken cancellationToken = default)
  {
    var fullProjectPath = Path.GetFullPath(projectPath);
    var (success, _, _) = await processQueue.RunProcessAsync(
        "dotnet",
        $"add \"{fullProjectPath}\" reference \"{Path.GetFullPath(targetPath)}\"",
        new ProcessOptions(true),
        cancellationToken);

    if (success)
    {
      buildHostManager.InvalidateCache(fullProjectPath);
    }

    return success;
  }

  public async Task<bool> RemoveProjectReferenceAsync(string projectPath, string targetPath, CancellationToken cancellationToken = default)
  {
    var fullProjectPath = Path.GetFullPath(projectPath);
    var (success, _, _) = await processQueue.RunProcessAsync(
        "dotnet",
        $"remove \"{fullProjectPath}\" reference \"{Path.GetFullPath(targetPath)}\"",
        new ProcessOptions(true),
        cancellationToken);

    if (success)
    {
      buildHostManager.InvalidateCache(fullProjectPath);
    }

    return success;
  }
}
