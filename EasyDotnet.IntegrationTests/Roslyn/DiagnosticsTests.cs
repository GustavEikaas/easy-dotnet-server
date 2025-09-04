using EasyDotnet.Controllers.Roslyn;
using EasyDotnet.IntegrationTests.Utils;

namespace EasyDotnet.IntegrationTests.Roslyn;

public class DiagnosticsTests
{
  private async Task<List<DiagnosticMessage>> GetDiagnosticsAsync(
    string targetPath,
    bool includeWarnings = false,
    CancellationToken cancellationToken = default)
  {
    using var server = await RpcTestServerInstantiator.GetInitializedStreamServer();

    var request = new { targetPath, includeWarnings };

    var diagnosticsStream = await server.InvokeWithParameterObjectAsync<IAsyncEnumerable<DiagnosticMessage>>(
      "roslyn/get-workspace-diagnostics",
      request,
      cancellationToken
    );

    var diagnostics = new List<DiagnosticMessage>();
    await foreach (var diagnostic in diagnosticsStream.WithCancellation(cancellationToken))
    {
      diagnostics.Add(diagnostic);
    }
    return diagnostics;
  }

  [Fact]
  public async Task GetDiagnostics_WithErrors_ReturnsErrorDiagnostics()
  {
    using var tempProject = new TempDotNetProjectWithError();
    using var cts = new CancellationTokenSource();

    var diagnostics = await GetDiagnosticsAsync(tempProject.CsprojPath, false, cts.Token);

    Assert.NotEmpty(diagnostics);

    var errorDiagnostic = diagnostics.FirstOrDefault(d => d.Code == "CS0103");
    Assert.NotNull(errorDiagnostic);
    Assert.Equal(1, errorDiagnostic.Severity);
    Assert.Contains("UndefinedVariable", errorDiagnostic.Message);
    Assert.Equal("roslyn", errorDiagnostic.Source);
    Assert.NotNull(errorDiagnostic.FilePath);
    Assert.EndsWith("ProgramWithError.cs", errorDiagnostic.FilePath);
  }

  [Fact]
  public async Task GetDiagnostics_WithWarnings_ReturnsWarningsWhenRequested()
  {
    using var tempProject = new TempDotNetProjectWithWarning();
    using var cts = new CancellationTokenSource();

    var diagnosticsWithoutWarnings = await GetDiagnosticsAsync(tempProject.CsprojPath, false, cts.Token);
    var warningDiagnostic = diagnosticsWithoutWarnings.FirstOrDefault(d => d.Code == "CS0219");
    Assert.Null(warningDiagnostic);

    var diagnosticsWithWarnings = await GetDiagnosticsAsync(tempProject.CsprojPath, includeWarnings: true, cts.Token);
    warningDiagnostic = diagnosticsWithWarnings.FirstOrDefault(d => d.Code == "CS0219");
    Assert.NotNull(warningDiagnostic);
    Assert.Equal(2, warningDiagnostic.Severity);
    Assert.Contains("unusedVariable", warningDiagnostic.Message);
  }

  [Fact]
  public async Task GetDiagnostics_ValidProject_ReturnsCorrectPositions()
  {
    using var tempProject = new TempDotNetProjectWithError();
    using var cts = new CancellationTokenSource();

    var diagnostics = await GetDiagnosticsAsync(tempProject.CsprojPath, includeWarnings: true, cts.Token);
    var errorDiagnostic = diagnostics.FirstOrDefault(d => d.Code == "CS0103");

    Assert.NotNull(errorDiagnostic);
    Assert.NotNull(errorDiagnostic.Range);

    Assert.True(errorDiagnostic.Range.Start.Line >= 0);
    Assert.True(errorDiagnostic.Range.Start.Character >= 0);
    Assert.True(errorDiagnostic.Range.End.Line >= 0);
    Assert.True(errorDiagnostic.Range.End.Character >= 0);

    Assert.Equal(6, errorDiagnostic.Range.Start.Line);
  }

  [Fact]
  public async Task GetDiagnostics_WithSolutionFile_ReturnsAllProjectDiagnostics()
  {
    using var tempSolution = new TempDotNetSolution();
    using var cts = new CancellationTokenSource();

    var diagnostics = await GetDiagnosticsAsync(tempSolution.SolutionPath, false, cts.Token);
    Assert.NotEmpty(diagnostics);

    var project1Errors = diagnostics.Where(d => d.FilePath.Contains("Project1")).ToList();
    var project2Errors = diagnostics.Where(d => d.FilePath.Contains("Project2")).ToList();

    Assert.NotEmpty(project1Errors);
    Assert.NotEmpty(project2Errors);
  }

  private class TempDotNetProjectWithError : TempDotNetProject
  {
    public TempDotNetProjectWithError() : base("TestProjectWithError")
    {
      var programWithErrorCode = @"using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine(UndefinedVariable);
    }
}";
      File.WriteAllText(Path.Combine(ProjectDirectory, "ProgramWithError.cs"), programWithErrorCode);
      File.Delete(ProgramCsPath);
    }
  }

  private class TempDotNetProjectWithWarning : TempDotNetProject
  {
    public TempDotNetProjectWithWarning() : base("TestProjectWithWarning")
    {
      var programWithWarningCode = @"using System;

class Program
{
    static void Main(string[] args)
    {
        int unusedVariable = 42;
        Console.WriteLine(""Hello, World!"");
    }
}";
      File.WriteAllText(Path.Combine(ProjectDirectory, "ProgramWithWarning.cs"), programWithWarningCode);
      File.Delete(ProgramCsPath);
    }
  }

  private class TempDotNetSolution : IDisposable
  {
    public string SolutionDirectory { get; }
    public string SolutionPath { get; }
    private readonly TempDotNetProject _project1;
    private readonly TempDotNetProject _project2;

    public TempDotNetSolution()
    {
      SolutionDirectory = Path.Combine(Path.GetTempPath(), $"TestSolution_{Guid.NewGuid()}");
      Directory.CreateDirectory(SolutionDirectory);
      SolutionPath = Path.Combine(SolutionDirectory, "TestSolution.sln");

      _project1 = new TempDotNetProject("Project1");
      _project2 = new TempDotNetProject("Project2");

      var project1ErrorCode = @"using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine(Error1);
    }
}";
      File.WriteAllText(Path.Combine(_project1.ProjectDirectory, "ProgramWithError.cs"), project1ErrorCode);
      File.Delete(_project1.ProgramCsPath);

      var project2ErrorCode = @"using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine(Error2);
    }
}";
      File.WriteAllText(Path.Combine(_project2.ProjectDirectory, "ProgramWithError.cs"), project2ErrorCode);
      File.Delete(_project2.ProgramCsPath);

      CreateSolutionFile();
    }

    private void CreateSolutionFile()
    {
      var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""Project1"", ""{_project1.CsprojPath}"", ""{{11111111-1111-1111-1111-111111111111}}""
EndProject
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""Project2"", ""{_project2.CsprojPath}"", ""{{22222222-2222-2222-2222-222222222222}}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{{11111111-1111-1111-1111-111111111111}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{{11111111-1111-1111-1111-111111111111}}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{{11111111-1111-1111-1111-111111111111}}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{{11111111-1111-1111-1111-111111111111}}.Release|Any CPU.Build.0 = Release|Any CPU
		{{22222222-2222-2222-2222-222222222222}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{{22222222-2222-2222-2222-222222222222}}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{{22222222-2222-2222-2222-222222222222}}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{{22222222-2222-2222-2222-222222222222}}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
";
      File.WriteAllText(SolutionPath, solutionContent);
    }

    public void Dispose()
    {
      _project1?.Dispose();
      _project2?.Dispose();

      if (Directory.Exists(SolutionDirectory))
      {
        Directory.Delete(SolutionDirectory, recursive: true);
      }
    }
  }
}