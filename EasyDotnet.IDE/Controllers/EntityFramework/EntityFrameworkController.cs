using System;
using System.Linq;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.Infrastructure.EntityFramework;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.EntityFramework;

public class EntityFrameworkController(
  ISolutionService solutionService,
  IClientService clientService,
  EntityFrameworkService enityFrameworkService,
  IEditorService editorService,
  IProgressScopeFactory progressScopeFactory) : BaseController
{

  [JsonRpcMethod("ef/database-update")]
  public async Task UpdateDatabase()
  {
    var solutionFile = clientService.RequireSolutionFile();
    var projects = solutionService.GetProjectsFromSolutionFile(solutionFile);

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
    var dbContexts = await enityFrameworkService.ListDbContextsAsync(efProject.Id, startupProject.Id, ".");
    scope.Dispose();
    if (dbContexts.Count == 0)
    {
      throw new Exception("no db contexts found");
    }
    if (dbContexts.Count == 1)
    {
      await editorService.RequestRunCommand(new RunCommand("dotnet-ef", ["database", "update", "--project", efProject.Id, "--startup-project", startupProject.Id, "--context", dbContexts[0].FullName], ".", []));
    }
    else
    {
      //TODO: build with qf list and pass --no-build
      var selectedContext = await editorService.RequestSelection("Select db context", [.. dbContexts.Select(x => new SelectionOption(x.FullName, x.Name))]) ?? throw new InvalidOperationException("Nothing selected");
      await editorService.RequestRunCommand(new RunCommand("dotnet-ef", ["database", "update", "--project", efProject.Id, "--startup-project", startupProject.Id, "--context", selectedContext.Id], ".", []));
    }
  }
}
