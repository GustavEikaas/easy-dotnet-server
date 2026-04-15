namespace EasyDotnet.ContainerTests.Scaffold;

/// <summary>
/// Creates a temporary .NET solution in /tmp with two console projects, each containing two .cs files.
/// Because /tmp is bind-mounted into the container, the server running inside the container sees
/// these files at the exact same absolute paths.
/// </summary>
public sealed class TempContainerSolution : IDisposable
{
  private readonly string _root;

  public string SolutionPath { get; }
  public string Project1Dir { get; }
  public string Project2Dir { get; }

  /// <summary>
  /// A standalone .cs file placed in a Scripts/ folder outside any project directory.
  /// Used to verify that workspace/run includes it as a script option in the picker.
  /// </summary>
  public string StandaloneFilePath { get; }

  public TempContainerSolution(string? globalJsonSdkVersion = null, string? globalJsonRollForward = null)
  {
    _root = Path.Combine(Path.GetTempPath(), $"ContainerTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_root);

    Project1Dir = Path.Combine(_root, "ProjectAlpha");
    Project2Dir = Path.Combine(_root, "ProjectBeta");
    Directory.CreateDirectory(Project1Dir);
    Directory.CreateDirectory(Project2Dir);

    SolutionPath = Path.Combine(_root, "TestSolution.slnx");

    var scriptsDir = Path.Combine(_root, "Scripts");
    Directory.CreateDirectory(scriptsDir);
    StandaloneFilePath = Path.Combine(scriptsDir, "Hello.cs");

    WriteProject(Project1Dir, "ProjectAlpha");
    WriteProject(Project2Dir, "ProjectBeta");
    WriteStandaloneFile(StandaloneFilePath);
    WriteSolution();

    if (globalJsonSdkVersion is not null)
      WriteGlobalJson(globalJsonSdkVersion, globalJsonRollForward ?? "latestFeature");
  }

  private static void WriteProject(string dir, string name)
  {
    File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <OutputType>Exe</OutputType>
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

  private static void WriteStandaloneFile(string path) =>
    File.WriteAllText(path, """Console.WriteLine("Hello from standalone script!");""");

  private void WriteGlobalJson(string sdkVersion, string rollForward) =>
    File.WriteAllText(Path.Combine(_root, "global.json"), $$"""
      {
        "sdk": {
          "version": "{{sdkVersion}}",
          "rollForward": "{{rollForward}}"
        }
      }
      """);

  private void WriteSolution()
  {
    var proj1Path = Path.Combine(Project1Dir, "ProjectAlpha.csproj");
    var proj2Path = Path.Combine(Project2Dir, "ProjectBeta.csproj");

    File.WriteAllText(SolutionPath, $"""
      <Solution>
        <Project Path="{proj1Path}" />
        <Project Path="{proj2Path}" />
      </Solution>
      """);
  }

  /// <summary>
  /// Writes a <c>Properties/launchSettings.json</c> file inside the given project directory.
  /// Creates the <c>Properties/</c> subdirectory if it does not exist.
  /// </summary>
  public static void WriteProjectLaunchSettings(string projectDir, string launchSettingsJson)
  {
    var propertiesDir = Path.Combine(projectDir, "Properties");
    Directory.CreateDirectory(propertiesDir);
    File.WriteAllText(Path.Combine(propertiesDir, "launchSettings.json"), launchSettingsJson);
  }

  /// <summary>
  /// Rewrites the solution file to remove <paramref name="projectDir"/>'s project entry.
  /// The project files on disk are left intact — simulating a project removed from the
  /// solution without being deleted, which is the canonical "stale persisted default" scenario.
  /// </summary>
  public void RemoveProjectFromSolution(string projectDir)
  {
    var csproj = Directory.EnumerateFiles(projectDir, "*.csproj").First();
    var otherProjects = new[] { Project1Dir, Project2Dir }
        .Where(d => !string.Equals(d, projectDir, StringComparison.OrdinalIgnoreCase))
        .Select(d => Directory.EnumerateFiles(d, "*.csproj").First())
        .ToList();

    File.WriteAllText(SolutionPath, $"""
      <Solution>
      {string.Join(Environment.NewLine, otherProjects.Select(p => $"  <Project Path=\"{p}\" />"))}
      </Solution>
      """);
  }

  public void Dispose()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }
}