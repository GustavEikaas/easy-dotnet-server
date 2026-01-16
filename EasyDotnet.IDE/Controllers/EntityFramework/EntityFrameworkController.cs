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
  EntityFrameworkService entityFrameworkService,
  IEditorService editorService,
  IProgressScopeFactory progressScopeFactory) : BaseController
{
  [JsonRpcMethod("ef/migrations-add")]
  public async Task AddMigration(string? migrationName = null)
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync();
    migrationName ??= await editorService.RequestString("Enter migration name", null);
    if (migrationName is null) return;

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["migrations", "add", migrationName, "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  [JsonRpcMethod("ef/migrations-remove")]
  public async Task RemoveMigration()
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync();

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["migrations", "remove", "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  [JsonRpcMethod("ef/migrations-apply")]
  public async Task ApplyMigration()
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync();

    using var migrationScope = progressScopeFactory.Create("Listing migrations", "Resolving migrations");
    var migrations = await entityFrameworkService.ListMigrationsAsync(efProject, startupProject, dbContext);
    migrationScope.Dispose();

    if (migrations.Count == 0)
    {
      throw new Exception("No migrations found");
    }

    var selectedMigration = await editorService.RequestSelection(
      "Select migration to apply",
      [.. migrations.Select(m => new SelectionOption(m.Id, m.Name))])
      ?? throw new InvalidOperationException("No migration selected");

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["database", "update", selectedMigration.Id, "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  [JsonRpcMethod("ef/database-update")]
  public async Task UpdateDatabase()
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync();

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["database", "update", "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  [JsonRpcMethod("ef/database-drop")]
  public async Task DropDatabase()
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync();

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["database", "update", "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  private async Task<(string EfProject, string StartupProject, string DbContext)> PromptEfProjectInfoAsync()
  {
    var solutionFile = clientService.RequireSolutionFile();
    var projects = solutionService.GetProjectsFromSolutionFile(solutionFile);

    var efProject = await editorService.RequestSelection(
      "Pick project",
      [.. projects.Select(x => new SelectionOption(x.AbsolutePath, x.ProjectName))]);

    if (efProject is null)
    {
      throw new InvalidOperationException("No EF project selected");
    }

    var startupProject = await editorService.RequestSelection(
      "Pick startup project",
      [.. projects.Select(x => new SelectionOption(x.AbsolutePath, x.ProjectName))]);

    if (startupProject is null)
    {
      throw new InvalidOperationException("No startup project selected");
    }

    using var scope = progressScopeFactory.Create("Listing db contexts", "Resolving db contexts");
    var dbContexts = await entityFrameworkService.ListDbContextsAsync(efProject.Id, startupProject.Id, ".");
    scope.Dispose();

    if (dbContexts.Count == 0)
    {
      throw new Exception("No db contexts found");
    }

    if (dbContexts.Count == 1)
    {
      return (efProject.Id, startupProject.Id, dbContexts[0].FullName);
    }

    var selectedContext = await editorService.RequestSelection(
      "Select db context",
      [.. dbContexts.Select(x => new SelectionOption(x.FullName, x.Name))])
      ?? throw new InvalidOperationException("No db context selected");

    return (efProject.Id, startupProject.Id, selectedContext.Id);
  }
}