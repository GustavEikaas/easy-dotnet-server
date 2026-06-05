namespace EasyDotnet.ContainerTests.EntityFramework;

[Collection(EasyDotnet.ContainerTests.Docker.ContainerCollections.Sdk10Linux)]
public sealed class EntityFrameworkDatabaseTests : EntityFrameworkTestBase<EasyDotnet.ContainerTests.Docker.Sdk10LinuxContainer>
{
  [Fact]
  public async Task UpdateDatabase_RunsDatabaseUpdate()
  {
    using var ws = CreateEfWorkspace();
    await InitializeWorkspaceAsync(ws);

    await BeginUpdateDatabase();
    var job = await ReceiveRunCommandAsync();

    Assert.Equal("dotnet-ef", job.Command.Executable);
    Assert.Equal(["database", "update"], job.Command.Arguments[..2]);
    Assert.Contains("--no-build", job.Command.Arguments);
  }

  [Fact]
  public async Task DropDatabase_RunsDatabaseDrop()
  {
    using var ws = CreateEfWorkspace();
    await InitializeWorkspaceAsync(ws);

    await BeginDropDatabase();
    var job = await ReceiveRunCommandAsync();

    Assert.Equal("dotnet-ef", job.Command.Executable);
    Assert.Equal(["database", "drop"], job.Command.Arguments[..2]);
    Assert.Contains("--no-build", job.Command.Arguments);
  }
}