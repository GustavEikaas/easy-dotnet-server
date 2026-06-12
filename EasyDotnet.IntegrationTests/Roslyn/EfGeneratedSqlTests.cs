using EasyDotnet.Controllers.Roslyn;
using EasyDotnet.IntegrationTests.Initialize;
using EasyDotnet.IntegrationTests.Utils;

namespace EasyDotnet.IntegrationTests.Roslyn;

public class EfGeneratedSqlTests
{
  [Fact]
  public async Task GetEfGeneratedSql_QueryUnderCursor_ReturnsSelectStatement()
  {
    using var tempProject = new TempEfCoreProject();
    var (line, character) = tempProject.GetQueryCursor();

    var response = await RpcTestServerInstantiator.InitializedOneShotRequest<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = tempProject.ProgramCsPath, line, character });

    Assert.True(response.Success, response.ErrorMessage);
    Assert.Contains("SELECT", response.Sql);
    Assert.Contains("Blogs", response.Sql);
    Assert.Contains("minId", response.Sql);
  }

  [Fact]
  public async Task GetEfGeneratedSql_CursorOnTerminatingOperator_ReturnsSelectStatement()
  {
    using var tempProject = new TempEfCoreProject();
    var (line, character) = tempProject.GetCursor("ToListAsync");

    var response = await RpcTestServerInstantiator.InitializedOneShotRequest<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = tempProject.ProgramCsPath, line, character });

    Assert.True(response.Success, response.ErrorMessage);
    Assert.Contains("SELECT", response.Sql);
    Assert.Contains("Blogs", response.Sql);
    Assert.Contains("minId", response.Sql);
  }

  [Fact]
  public async Task GetEfGeneratedSql_ProjectionToDtoInEnclosingNamespace_ReturnsSelectStatement()
  {
    using var tempProject = new TempEfCoreProject();
    var (line, character) = tempProject.GetCursor("Select");

    var response = await RpcTestServerInstantiator.InitializedOneShotRequest<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = tempProject.ProgramCsPath, line, character });

    Assert.True(response.Success, response.ErrorMessage);
    Assert.Contains("SELECT", response.Sql);
    Assert.Contains("Title", response.Sql);
  }

  [Fact]
  public async Task GetEfGeneratedSql_ProjectionToNestedDto_ReturnsSelectStatement()
  {
    using var tempProject = new TempEfCoreProject();
    var (line, character) = tempProject.GetCursor("new NestedDto");

    var response = await RpcTestServerInstantiator.InitializedOneShotRequest<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = tempProject.ProgramCsPath, line, character });

    Assert.True(response.Success, response.ErrorMessage);
    Assert.Contains("SELECT", response.Sql);
    Assert.Contains("Title", response.Sql);
  }

  [Fact]
  public async Task GetEfGeneratedSql_RepeatCallAfterFileEdit_UsesCachedWorkspaceAndReflectsChange()
  {
    using var tempProject = new TempEfCoreProject();
    using var server = await RpcTestServerInstantiator.GetInitializedStreamServer();
    var (line, character) = tempProject.GetQueryCursor();

    var first = await server.InvokeWithParameterObjectAsync<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = tempProject.ProgramCsPath, line, character });

    Assert.True(first.Success, first.ErrorMessage);
    Assert.Contains(">", first.Sql);

    var source = File.ReadAllText(tempProject.ProgramCsPath);
    File.WriteAllText(tempProject.ProgramCsPath, source.Replace("b.Id > minId", "b.Id < minId"));

    var second = await server.InvokeWithParameterObjectAsync<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = tempProject.ProgramCsPath, line, character });

    Assert.True(second.Success, second.ErrorMessage);
    Assert.Contains("<", second.Sql);
  }

  [Fact]
  public async Task GetEfGeneratedSql_NoQueryAtCursor_ReturnsError()
  {
    using var tempProject = new TempEfCoreProject();

    var response = await RpcTestServerInstantiator.InitializedOneShotRequest<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = tempProject.ProgramCsPath, line = 0, character = 0 });

    Assert.False(response.Success);
    Assert.NotNull(response.ErrorMessage);
  }

  [Fact]
  public async Task GetEfGeneratedSql_NoStartupConfigured_FallsBackToFileProjectWithWarning()
  {
    using var tempProject = new TempEfCoreProject();
    var (line, character) = tempProject.GetQueryCursor();

    var response = await RpcTestServerInstantiator.InitializedOneShotRequest<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = tempProject.ProgramCsPath, line, character });

    Assert.True(response.Success, response.ErrorMessage);
    Assert.Equal(tempProject.CsprojPath, response.TargetProject);
    Assert.Equal(tempProject.CsprojPath, response.StartupProject);
    Assert.Equal("WorkspaceProjectResolver.ResolveAsync fallback", response.StartupProjectSource);
    Assert.NotEmpty(response.Warnings);
  }

  [Fact]
  public async Task GetEfGeneratedSql_WithConfiguredStartupProject_ActivatesContextViaStartupProject()
  {
    using var fixture = new TempEfCoreSolution();
    using var server = RpcTestServerInstantiator.GetUninitializedStreamServer();

    await server.InvokeWithParameterObjectAsync<TestInitializeResponse>(
      "initialize",
      new List<TestInitializeRequest> { new(new TestClientInfo("test", "3.0.0"), new TestProjectInfo(fixture.RootDir, fixture.SlnxPath)) });

    // The background solution load does a read-modify-write of the solution settings file;
    // wait for it so set-default-startup-project does not get clobbered (last-writer-wins).
    var deadline = DateTime.UtcNow.AddSeconds(90);
    while (!File.Exists(fixture.SettingsFilePath) && DateTime.UtcNow < deadline)
    {
      await Task.Delay(250);
    }

    await server.InvokeWithParameterObjectAsync<object?>(
      "set-default-startup-project",
      new { projectPath = fixture.ApiCsprojPath });

    var (line, character) = fixture.GetCursor("Where");
    var response = await server.InvokeWithParameterObjectAsync<EfGeneratedSqlResponse>(
      "roslyn/ef-generated-sql",
      new { sourceFilePath = fixture.QueriesCsPath, line, character });

    Assert.True(response.Success, response.ErrorMessage);
    Assert.Contains("SELECT", response.Sql);
    Assert.Contains("minId", response.Sql);
    Assert.Equal(fixture.DataCsprojPath, response.TargetProject);
    Assert.Equal(fixture.ApiCsprojPath, response.StartupProject);
    Assert.Equal("SettingsService.DefaultStartupProject", response.StartupProjectSource);
    Assert.Empty(response.Warnings);
  }
}