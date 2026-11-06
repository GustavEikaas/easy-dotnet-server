namespace EasyDotnet.ContainerTests.TestRunner.Fixtures;

/// <summary>
/// A <c>.slnx</c> under <c>/tmp</c> that references several prebuilt fixtures, for tests
/// that need more than one project in a solution. Dispose deletes only the solution
/// directory; each fixture cleans up after itself.
/// </summary>
public sealed class MultiProjectSolution : IDisposable
{
  public string RootDir { get; }
  public string SolutionPath { get; }

  private MultiProjectSolution(string rootDir, string solutionPath)
  {
    RootDir = rootDir;
    SolutionPath = solutionPath;
  }

  public static MultiProjectSolution Create(params TestProjectFixture[] fixtures)
  {
    var root = Path.Combine(Path.GetTempPath(), $"MultiProjectSolution_{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);

    var solutionPath = Path.Combine(root, "Combined.slnx");
    var projects = string.Join("\n", fixtures.Select(f => $"""  <Project Path="{f.CsprojPath}" />"""));
    File.WriteAllText(solutionPath, $"""
      <Solution>
      {projects}
      </Solution>
      """);

    return new MultiProjectSolution(root, solutionPath);
  }

  public void Dispose()
  {
    try { Directory.Delete(RootDir, recursive: true); } catch { }
  }
}