namespace EasyDotnet.ContainerTests.Scaffold;

/// <summary>
/// A minimal temp directory for no-solution workspace tests.
/// Unlike <see cref="TempContainerSolution"/>, this creates no .sln/.slnx file,
/// so the server falls through to its heuristic project-discovery and single-file paths.
/// /tmp is bind-mounted host↔container so all paths are shared.
/// </summary>
public sealed class TempContainerWorkspace : IDisposable
{
  public string RootDir { get; }

  public TempContainerWorkspace()
  {
    RootDir = Path.Combine(Path.GetTempPath(), $"ContainerTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(RootDir);
  }

  /// <summary>
  /// Adds a console project in a subdirectory under the workspace root.
  /// Returns the project directory path.
  /// </summary>
  public string AddProject(string name)
  {
    var dir = Path.Combine(RootDir, name);
    Directory.CreateDirectory(dir);

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

    return dir;
  }

  /// <summary>
  /// Adds a standalone .cs file at the given path relative to the workspace root.
  /// Returns the absolute path.
  /// </summary>
  public string AddStandaloneFile(string relativePath)
  {
    var fullPath = Path.Combine(RootDir, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, """Console.WriteLine("Hello from standalone file!");""");
    return fullPath;
  }

  public void Dispose()
  {
    if (Directory.Exists(RootDir))
    {
      Directory.Delete(RootDir, recursive: true);
    }
  }
}