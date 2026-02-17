using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.Services;
using EasyDotnet.Infrastructure.Editor;
using EasyDotnet.Infrastructure.EntityFramework;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.EntityFramework;

public class EntityFrameworkController(
  ISolutionService solutionService,
  IClientService clientService,
  EntityFrameworkService entityFrameworkService,
  DbContextCache dbContextCache,
  IEditorService editorService,
  IProgressScopeFactory progressScopeFactory) : BaseController
{
  [JsonRpcMethod("ef/migrations-add")]
  public async Task AddMigration(string? migrationName = null, CancellationToken cancellationToken = default)
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync(cancellationToken);
    migrationName ??= await editorService.RequestString("Enter migration name", null);
    if (migrationName is null) return;

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["migrations", "add", migrationName, "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  [JsonRpcMethod("ef/migrations-remove")]
  public async Task RemoveMigration(CancellationToken cancellationToken)
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync(cancellationToken);

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["migrations", "remove", "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  [JsonRpcMethod("ef/migrations-apply")]
  public async Task ApplyMigration(CancellationToken cancellationToken)
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync(cancellationToken);

    using var migrationScope = progressScopeFactory.Create("Listing migrations", "Resolving migrations");
    var migrations = await entityFrameworkService.ListMigrationsAsync(efProject, startupProject, dbContext, cancellationToken: cancellationToken);
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
  public async Task UpdateDatabase(CancellationToken cancellationToken)
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync(cancellationToken);

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["database", "update", "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  [JsonRpcMethod("ef/database-drop")]
  public async Task DropDatabase(CancellationToken cancellationToken)
  {
    var (efProject, startupProject, dbContext) = await PromptEfProjectInfoAsync(cancellationToken);

    await editorService.RequestRunCommand(new RunCommand(
      "dotnet-ef",
      ["database", "update", "--project", efProject, "--startup-project", startupProject, "--context", dbContext],
      ".",
      []));
  }

  private async Task<(string EfProject, string StartupProject, string DbContext)> PromptEfProjectInfoAsync(CancellationToken cancellationToken)
  {
    var solutionFile = clientService.RequireSolutionFile();
    var projects = await solutionService.GetProjectsFromSolutionFile(solutionFile, cancellationToken);

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

    var cached = await dbContextCache.TryGetAsync(efProj: efProject.Id, startupProj: startupProject.Id);

    if (cached is not null && cached.Count != 0)
    {
      var scanOption = new SelectionOption("SCAN", "🔄️ Scan for more dbcontexts");
      var options = cached.Select(x => new SelectionOption(x.FullName, x.Name)).Concat([scanOption]);
      var selection = await editorService.RequestSelection("Select db context", [.. options]) ?? throw new InvalidOperationException("No db context selected");
      if (selection.Id != scanOption.Id)
      {
        return (efProject.Id, startupProject.Id, selection.Id);
      }
    }

    var dbContexts = await ScanForContextsAsync(efProject: efProject.Id, startupProject: startupProject.Id, cancellationToken);

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

  private async Task<List<DbContextInfo>> ScanForContextsAsync(string efProject, string startupProject, CancellationToken ct)
  {
    using var scope = progressScopeFactory.Create("Listing db contexts", "Resolving db contexts");

    var contexts = await entityFrameworkService.ListDbContextsAsync(efProject, startupProject, ".", ct);

    if (contexts.Count != 0)
    {
      await dbContextCache.SetAsync(efProject, startupProject, contexts);
    }

    return contexts;
  }
}