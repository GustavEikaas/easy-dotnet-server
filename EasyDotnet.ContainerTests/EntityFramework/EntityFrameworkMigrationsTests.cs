namespace EasyDotnet.ContainerTests.EntityFramework;

public sealed class EntityFrameworkMigrationsTests : EntityFrameworkTestBase<EfFakeDotnetSdk10LinuxContainer>
{
  [Fact]
  public async Task ListMigrations_OpensTimestampedMigrationFile()
  {
    using var ws = CreateEfWorkspace();
    var project = ws.Project("App");
    var expectedPath = Path.Combine(project.Dir, "Migrations", $"{MigrationId}.cs");

    await InitializeWorkspaceAsync(ws);

    var listTask = BeginListMigrations();
    var picker = await ReceivePickerAsync(_ => [MigrationId]);
    var openBuffer = await ReceiveOpenBufferAsync();
    await listTask;

    Assert.Contains(picker.Choices, c => c.Id == MigrationId);
    Assert.Equal(expectedPath, openBuffer.Path);
    Assert.True(File.Exists(openBuffer.Path), $"Expected openBuffer path to exist: {openBuffer.Path}");
  }

  [Fact]
  public async Task ListMigrations_WithNoMigrations_DisplaysMessageAndDoesNotShowPicker()
  {
    using var ws = CreateEfWorkspace(emptyMigrations: true);
    await InitializeWorkspaceAsync(ws);

    await BeginListMigrations();

    var message = await ReceiveDisplayMessageAsync();

    Assert.Equal("No migrations found", message);
    Assert.True(PickerNotReceived(), "picker/pick must not be called when there are no migrations");
  }

  [Fact]
  public async Task ApplyMigration_RunsDatabaseUpdateForSelectedMigrationId()
  {
    using var ws = CreateEfWorkspace();
    await InitializeWorkspaceAsync(ws);

    await BeginApplyMigration();
    var job = await ReceiveRunCommandAsync();

    Assert.Equal("dotnet-ef", job.Command.Executable);
    Assert.Equal("database", job.Command.Arguments[0]);
    Assert.Equal("update", job.Command.Arguments[1]);
    Assert.Equal(MigrationId, job.Command.Arguments[2]);
    Assert.Contains("--no-build", job.Command.Arguments);
  }

  [Fact]
  public async Task AddMigration_PromptsForNameAndRunsMigrationsAdd()
  {
    using var ws = CreateEfWorkspace();
    await InitializeWorkspaceAsync(ws);
    PromptStringResponse = "AddAuditLog";

    await BeginAddMigration();
    var job = await ReceiveRunCommandAsync();

    Assert.Equal("dotnet-ef", job.Command.Executable);
    Assert.Equal(["migrations", "add", "AddAuditLog"], job.Command.Arguments[..3]);
    Assert.Contains("--no-build", job.Command.Arguments);
  }

  [Fact]
  public async Task RemoveMigration_RunsMigrationsRemove()
  {
    using var ws = CreateEfWorkspace();
    await InitializeWorkspaceAsync(ws);

    await BeginRemoveMigration();
    var job = await ReceiveRunCommandAsync();

    Assert.Equal("dotnet-ef", job.Command.Executable);
    Assert.Equal(["migrations", "remove"], job.Command.Arguments[..2]);
    Assert.Contains("--no-build", job.Command.Arguments);
  }
}