using Microsoft.Build.Construction;

namespace EasyDotnet.IntegrationTests.Roslyn;

public class TempDotNetProject : IDisposable
{
  public string ProjectName { get; }
  public string ProjectDirectory { get; }
  public string CsprojPath => Path.Combine(ProjectDirectory, $"{ProjectName}.csproj");
  public string ProgramCsPath => Path.Combine(ProjectDirectory, "Program.cs");

  public TempDotNetProject(string projectName)
  {
    ProjectName = projectName;
    ProjectDirectory = Path.Combine(Path.GetTempPath(), $"{projectName}_{Guid.NewGuid()}");
    Directory.CreateDirectory(ProjectDirectory);
    CreateProject();
  }

  private void CreateProject()
  {
    var project = ProjectRootElement.Create();
    project.Sdk = "Microsoft.NET.Sdk";

    var propertyGroup = project.AddPropertyGroup();
    propertyGroup.AddProperty("OutputType", "Exe");
    propertyGroup.AddProperty("TargetFramework", "net8.0");

    project.Save(CsprojPath);

    var programCode = @"using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine(""Hello, World!"");
    }
}";
    File.WriteAllText(ProgramCsPath, programCode);
  }

  public void Dispose()
  {
    if (Directory.Exists(ProjectDirectory))
    {
      Directory.Delete(ProjectDirectory, recursive: true);
    }
  }
}