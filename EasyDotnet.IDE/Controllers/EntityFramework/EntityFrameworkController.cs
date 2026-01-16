using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.EntityFramework;

public class EntityFrameworkController(ISolutionService solutionService, IClientService clientService, IEditorService editorService, IProgressScopeFactory progressScopeFactory) : BaseController
{
  [JsonRpcMethod("ef/database-update")]
  public async Task UpdateDatabase()
  {
    if (clientService?.ProjectInfo?.SolutionFile is null)
    {
      throw new InvalidOperationException("Solution file is required for this to work");

    }
    var projects = solutionService.GetProjectsFromSolutionFile(clientService.ProjectInfo.SolutionFile);

    var efProject = await editorService.RequestSelection("Pick project", [.. projects.Select(x => new SelectionOption(x.AbsolutePath, x.ProjectName))]);
    if (efProject is null)
    {
      return;
    }

    var startupProject = await editorService.RequestSelection("Pick startup project", [.. projects.Select(x => new SelectionOption(x.AbsolutePath, x.ProjectName))]);
    if (startupProject is null)
    {
      return;
    }
    using var scope = progressScopeFactory.Create("Listing db contexts", "Resolving db contexts");
    var dbContexts = await ListDbContextsAsync(efProject.Id, startupProject.Id, ".");
    scope.Dispose();
    // if (dbContexts.Count == 0)
    // {
    //   throw new Exception("no db contexts found");
    // }
    // if (dbContexts.Count == 1)
    // {
    //   await editorService.RequestRunCommand(new RunCommand("dotnet-ef", ["database", "update", "--project", efProject.Id, "--startup-project", startupProject.Id, "--context", dbContexts[0].FullName], ".", new()));
    // }
    // else
    // {
    var selectedContext = await editorService.RequestSelection("Select db context", dbContexts.Select(x => new SelectionOption(x.FullName, x.Name)).ToArray());
    if (selectedContext is null)
    {
      throw new InvalidOperationException("Nothing selected");
    }
    await editorService.RequestRunCommand(new RunCommand("dotnet-ef", ["database", "update", "--project", efProject.Id, "--startup-project", startupProject.Id, "--context", selectedContext.Id], ".", new()));
    // }
  }

  [JsonRpcMethod("ef/database-drop")]
  public async Task DropDatabase()
  {

  }


  public async Task<List<DbContextInfo>> ListDbContextsAsync(
      string efProjectPath,
      string startupProjectPath,
      string workingDirectory = ".")
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet-ef",
      Arguments = $"dbcontext list --project \"{efProjectPath}\" --startup-project \"{startupProjectPath}\" --json --prefix-output",
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = new Process { StartInfo = startInfo };

    var stdoutBuilder = new StringBuilder();
    var stderrBuilder = new StringBuilder();

    process.OutputDataReceived += (sender, e) =>
    {
      if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
    };

    process.ErrorDataReceived += (sender, e) =>
    {
      if (e.Data != null) stderrBuilder.AppendLine(e.Data);
    };


    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    await process.WaitForExitAsync();
    var stdout = stdoutBuilder.ToString();
    var stderr = stderrBuilder.ToString();

    if (process.ExitCode != 0)
    {
      throw new Exception(
          $"EF command failed with exit code {process.ExitCode}");
    }

    // Parse the JSON output
    return ParseDbContextOutput(stdout);
  }
  private List<DbContextInfo> ParseDbContextOutput(string output)
  {
    var dataLines = output
        .Split('\n')
        .Where(line => line.TrimStart().StartsWith("data:"))
        .Select(line => line.TrimStart().Substring(5).TrimStart())
        .ToList();

    var jsonString = string.Join("", dataLines);

    if (string.IsNullOrWhiteSpace(jsonString))
    {
      return new List<DbContextInfo>();
    }

    var contexts = JsonSerializer.Deserialize<List<DbContextInfo>>(jsonString, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    return contexts ?? new List<DbContextInfo>();
  }
}

public record DbContextInfo(string FullName, string SafeName, string Name, string AssemblyQualifiedName);