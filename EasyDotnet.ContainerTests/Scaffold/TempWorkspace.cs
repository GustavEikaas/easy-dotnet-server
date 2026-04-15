namespace EasyDotnet.ContainerTests.Scaffold;

/// <summary>
/// Fluent builder for temporary .NET workspaces used in container integration tests.
/// <para>
/// /tmp is bind-mounted host↔container so all paths produced by <see cref="Build"/> are visible
/// to both the test process and the server running inside Docker at the exact same absolute paths.
/// </para>
/// <example>
/// Two-project .slnx solution with inline launch settings:
/// <code>
/// using var ws = new TempWorkspaceBuilder()
///     .WithSolutionX()
///     .WithProject("ProjectAlpha", p => p.WithLaunchSettings(json))
///     .WithProject("ProjectBeta")
///     .Build();
/// ws.Project("ProjectAlpha").Dir   // absolute directory
/// ws.SolutionPath                  // absolute .slnx path
/// </code>
/// No-solution workspace (heuristic project discovery):
/// <code>
/// using var ws = new TempWorkspaceBuilder()
///     .WithProject("AppAlpha")
///     .WithProject("AppBeta")
///     .Build();
/// </code>
/// Multi-solution workspace:
/// <code>
/// using var ws = new TempWorkspaceBuilder()
///     .WithSolutionX("SolutionA")
///         .WithProject("ProjectAlpha")
///     .WithSolutionX("SolutionB")
///         .WithProject("ProjectBeta")
///     .Build();
/// </code>
/// </example>
/// </summary>
public sealed class TempWorkspaceBuilder
{
  private readonly List<SolutionSpec> _solutions = [];
  private readonly List<ProjectSpec> _projects = [];
  private string? _singleFileRelativePath;
  private string? _globalJsonSdkVersion;
  private string? _globalJsonRollForward;

  /// <summary>Declares a <c>.slnx</c> solution file at <paramref name="relativePath"/> (relative to workspace root).</summary>
  public TempWorkspaceBuilder WithSolutionX(string relativePath = "Solution")
  {
    _solutions.Add(new SolutionSpec(relativePath, isSlnx: true));
    return this;
  }

  /// <summary>Declares a legacy <c>.sln</c> solution file at <paramref name="relativePath"/> (relative to workspace root).</summary>
  public TempWorkspaceBuilder WithSolution(string relativePath = "Solution")
  {
    _solutions.Add(new SolutionSpec(relativePath, isSlnx: false));
    return this;
  }

  /// <summary>
  /// Adds a console project at <paramref name="relativePath"/> (relative to workspace root).
  /// The last path segment is the project name and the key for <see cref="TempWorkspace.Project"/>.
  /// If a solution has been declared, the project is added to the most recently declared solution.
  /// </summary>
  public TempWorkspaceBuilder WithProject(string relativePath, Action<TempProjectBuilder>? configure = null)
  {
    var builder = new TempProjectBuilder();
    configure?.Invoke(builder);
    var spec = new ProjectSpec(relativePath, builder);
    _projects.Add(spec);

    if (_solutions.Count > 0)
      _solutions[^1].Projects.Add(spec);

    return this;
  }

  /// <summary>
  /// Adds a standalone <c>.cs</c> file at <paramref name="relativePath"/> (relative to workspace root).
  /// Accessible via <see cref="TempWorkspace.SingleFilePath"/>.
  /// </summary>
  public TempWorkspaceBuilder SingleFileProject(string relativePath)
  {
    _singleFileRelativePath = relativePath;
    return this;
  }

  /// <summary>Writes a <c>global.json</c> at the workspace root pinning the given SDK version.</summary>
  public TempWorkspaceBuilder WithGlobalJson(string sdkVersion, string rollForward = "latestFeature")
  {
    _globalJsonSdkVersion = sdkVersion;
    _globalJsonRollForward = rollForward;
    return this;
  }

  /// <summary>Materialises the workspace to disk and returns a <see cref="TempWorkspace"/> handle.</summary>
  public TempWorkspace Build()
  {
    var root = Path.Combine(Path.GetTempPath(), $"ContainerTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);

    var projectMap = new Dictionary<string, TempProject>(StringComparer.OrdinalIgnoreCase);
    foreach (var spec in _projects)
    {
      var dir = Path.Combine(root, spec.RelativePath);
      Directory.CreateDirectory(dir);
      WriteProject(dir, spec.Name, spec.Builder.OutputType);
      if (spec.Builder.LaunchSettingsJson is not null)
        TempProject.WriteLaunchSettingsTo(dir, spec.Builder.LaunchSettingsJson);
      projectMap[spec.Name] = new TempProject(dir, spec.Name);
    }

    var solutionPaths = new List<string>();
    foreach (var sol in _solutions)
    {
      var ext = sol.IsSlnx ? ".slnx" : ".sln";
      var solutionPath = Path.Combine(root, sol.RelativePath + ext);
      Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);
      WriteSolution(solutionPath, sol, root);
      solutionPaths.Add(solutionPath);
    }

    string? singleFilePath = null;
    if (_singleFileRelativePath is not null)
    {
      singleFilePath = Path.Combine(root, _singleFileRelativePath);
      Directory.CreateDirectory(Path.GetDirectoryName(singleFilePath)!);
      File.WriteAllText(singleFilePath, """Console.WriteLine("Hello from standalone script!");""");
    }

    if (_globalJsonSdkVersion is not null)
      File.WriteAllText(Path.Combine(root, "global.json"), $$"""
        {
          "sdk": {
            "version": "{{_globalJsonSdkVersion}}",
            "rollForward": "{{_globalJsonRollForward}}"
          }
        }
        """);

    return new TempWorkspace(root, solutionPaths, projectMap, singleFilePath);
  }

  private static void WriteProject(string dir, string name, string outputType = "Exe")
  {
    File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), $"""
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <OutputType>{outputType}</OutputType>
          <TargetFramework>net8.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
        </PropertyGroup>
      </Project>
      """);

    File.WriteAllText(Path.Combine(dir, "Program.cs"), $"""
      Console.WriteLine("Hello from {name}!");
      """);

    File.WriteAllText(Path.Combine(dir, "Helpers.cs"), $$"""
      namespace {{name}};

      internal static class Helpers
      {
          internal static string Greet(string name) => $"Hello, {name}!";
      }
      """);
  }

  private static void WriteSolution(string solutionPath, SolutionSpec sol, string root)
  {
    var entries = sol.Projects.Select(p =>
    {
      var csprojPath = Path.Combine(root, p.RelativePath, $"{p.Name}.csproj");
      return $"  <Project Path=\"{csprojPath}\" />";
    });

    File.WriteAllText(solutionPath, $"""
      <Solution>
      {string.Join(Environment.NewLine, entries)}
      </Solution>
      """);
  }

  private sealed class SolutionSpec(string relativePath, bool isSlnx)
  {
    public string RelativePath { get; } = relativePath;
    public bool IsSlnx { get; } = isSlnx;
    public List<ProjectSpec> Projects { get; } = [];
  }

  private sealed class ProjectSpec(string relativePath, TempProjectBuilder builder)
  {
    public string RelativePath { get; } = relativePath;
    public string Name { get; } = Path.GetFileName(relativePath.TrimEnd(Path.DirectorySeparatorChar, '/'));
    public TempProjectBuilder Builder { get; } = builder;
  }
}

/// <summary>
/// A temporary .NET workspace created by <see cref="TempWorkspaceBuilder"/>.
/// Dispose to delete the entire workspace directory.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
  private readonly List<string> _solutionPaths;
  private readonly Dictionary<string, TempProject> _projects;

  public string RootDir { get; }

  /// <summary>
  /// The single solution path, or <c>null</c> if no solution was declared.
  /// Throws <see cref="InvalidOperationException"/> when multiple solutions exist — use <see cref="Solutions"/> instead.
  /// </summary>
  public string? SolutionPath => _solutionPaths.Count switch
  {
    0 => null,
    1 => _solutionPaths[0],
    _ => throw new InvalidOperationException(
      $"Workspace has {_solutionPaths.Count} solutions — use Solutions to access them individually.")
  };

  /// <summary>All solution paths in declaration order.</summary>
  public IReadOnlyList<string> Solutions => _solutionPaths;

  /// <summary>Absolute path to the standalone <c>.cs</c> file, or <c>null</c> if none was added.</summary>
  public string? SingleFilePath { get; }

  internal TempWorkspace(string rootDir, List<string> solutionPaths, Dictionary<string, TempProject> projects, string? singleFilePath)
  {
    RootDir = rootDir;
    _solutionPaths = solutionPaths;
    _projects = projects;
    SingleFilePath = singleFilePath;
  }

  /// <summary>Returns the project registered under <paramref name="name"/> (the last segment of the relative path passed to <c>WithProject</c>).</summary>
  public TempProject Project(string name) => _projects[name];

  /// <summary>
  /// Rewrites the most recently declared solution file to remove <paramref name="projectName"/>'s project entry.
  /// Project files on disk are left intact — simulating a project removed from the solution without being deleted,
  /// which is the canonical "stale persisted default" scenario.
  /// </summary>
  public void RemoveFromSolution(string projectName)
  {
    if (_solutionPaths.Count == 0)
      throw new InvalidOperationException("No solutions in this workspace.");

    var solutionPath = _solutionPaths[^1];
    var csprojPath = _projects[projectName].CsprojPath;
    var lines = File.ReadAllLines(solutionPath)
      .Where(l => !l.Contains(csprojPath, StringComparison.OrdinalIgnoreCase))
      .ToArray();
    File.WriteAllLines(solutionPath, lines);
  }

  public void Dispose()
  {
    if (Directory.Exists(RootDir))
      Directory.Delete(RootDir, recursive: true);
  }
}

/// <summary>Configures a project during <see cref="TempWorkspaceBuilder.WithProject"/>.</summary>
public sealed class TempProjectBuilder
{
  internal string? LaunchSettingsJson { get; private set; }
  internal string OutputType { get; private set; } = "Exe";

  public TempProjectBuilder WithLaunchSettings(string json)
  {
    LaunchSettingsJson = json;
    return this;
  }

  /// <summary>
  /// Marks the project as a class library (<c>OutputType=Library</c>).
  /// Libraries are not runnable and will be filtered out of project pickers.
  /// </summary>
  public TempProjectBuilder AsLibrary()
  {
    OutputType = "Library";
    return this;
  }
}

/// <summary>A single project inside a <see cref="TempWorkspace"/>.</summary>
public sealed class TempProject
{
  public string Dir { get; }
  public string CsprojPath { get; }

  internal TempProject(string dir, string name)
  {
    Dir = dir;
    CsprojPath = Path.Combine(dir, $"{name}.csproj");
  }

  /// <summary>Writes (or overwrites) <c>Properties/launchSettings.json</c> for this project.</summary>
  public void WriteLaunchSettings(string launchSettingsJson) =>
    WriteLaunchSettingsTo(Dir, launchSettingsJson);

  internal static void WriteLaunchSettingsTo(string projectDir, string launchSettingsJson)
  {
    var propertiesDir = Path.Combine(projectDir, "Properties");
    Directory.CreateDirectory(propertiesDir);
    File.WriteAllText(Path.Combine(propertiesDir, "launchSettings.json"), launchSettingsJson);
  }
}