using System.IO.Abstractions;

namespace EasyDotnet.ProjXLanguageServer.Services;

public sealed record ProjXWorkspaceHierarchy(
    string ProjectPath,
    string? WorkspaceRoot,
    string? DirectoryBuildPropsPath,
    string? DirectoryBuildTargetsPath,
    bool ManagePackageVersionsCentrally,
    string? DirectoryPackagesPropsPath);

public interface IProjXWorkspaceHierarchyService
{
  Task<ProjXWorkspaceHierarchy> ResolveAsync(string projectPath, CancellationToken cancellationToken);
}

public sealed class ProjXWorkspaceHierarchyService(
    IProjXWorkspaceContext workspaceContext,
    IProjXMsBuildPropertyProvider propertyProvider,
    IFileSystem fileSystem) : IProjXWorkspaceHierarchyService
{
  public async Task<ProjXWorkspaceHierarchy> ResolveAsync(string projectPath, CancellationToken cancellationToken)
  {
    var fullProjectPath = fileSystem.Path.GetFullPath(projectPath);
    if (!fileSystem.File.Exists(fullProjectPath))
    {
      throw new FileNotFoundException("Project file not found.", fullProjectPath);
    }

    var projectDir = fileSystem.Path.GetDirectoryName(fullProjectPath)
      ?? throw new InvalidOperationException($"Could not resolve project directory for {fullProjectPath}.");
    var workspaceRoot = ResolveWorkspaceRoot(projectDir);

    var properties = await propertyProvider.GetPropertiesAsync(
        fullProjectPath,
        [
          "ManagePackageVersionsCentrally",
          "DirectoryPackagesPropsPath",
          "DirectoryBuildPropsPath",
          "DirectoryBuildTargetsPath"
        ],
        cancellationToken);

    properties.TryGetValue("ManagePackageVersionsCentrally", out var manageCentrallyRaw);
    properties.TryGetValue("DirectoryPackagesPropsPath", out var directoryPackagesPropsPath);
    properties.TryGetValue("DirectoryBuildPropsPath", out var directoryBuildPropsPath);
    properties.TryGetValue("DirectoryBuildTargetsPath", out var directoryBuildTargetsPath);

    return new ProjXWorkspaceHierarchy(
        ProjectPath: fullProjectPath,
        WorkspaceRoot: workspaceRoot,
        DirectoryBuildPropsPath: NullIfEmpty(directoryBuildPropsPath ?? string.Empty)
            ?? FindFileAbove(projectDir, "Directory.Build.props", workspaceRoot),
        DirectoryBuildTargetsPath: NullIfEmpty(directoryBuildTargetsPath ?? string.Empty)
            ?? FindFileAbove(projectDir, "Directory.Build.targets", workspaceRoot),
        ManagePackageVersionsCentrally: IsTrue(manageCentrallyRaw ?? string.Empty),
        DirectoryPackagesPropsPath: NullIfEmpty(directoryPackagesPropsPath ?? string.Empty));
  }

  private string? ResolveWorkspaceRoot(string projectDir)
  {
    var rootUri = workspaceContext.RootUri;
    if (rootUri is { IsFile: true })
    {
      return fileSystem.Path.GetFullPath(rootUri.LocalPath);
    }

    return projectDir;
  }

  private string? FindFileAbove(string startDirectory, string fileName, string? stopDirectory)
  {
    var dir = fileSystem.Path.GetFullPath(startDirectory);
    var stop = stopDirectory is null ? null : fileSystem.Path.GetFullPath(stopDirectory);

    while (!string.IsNullOrEmpty(dir))
    {
      var candidate = fileSystem.Path.Combine(dir, fileName);
      if (fileSystem.File.Exists(candidate))
      {
        return candidate;
      }

      if (stop is not null && SamePath(dir, stop))
      {
        break;
      }

      var parent = fileSystem.Directory.GetParent(dir)?.FullName;
      if (string.IsNullOrEmpty(parent) || SamePath(parent, dir))
      {
        break;
      }
      dir = parent;
    }

    return null;
  }

  private static bool IsTrue(string value) =>
      string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

  private static string? NullIfEmpty(string value) =>
      string.IsNullOrWhiteSpace(value) ? null : value;

  private static bool SamePath(string left, string right) =>
      string.Equals(
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);
}
