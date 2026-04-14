using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests;

public abstract class InitializeContainerTests<TContainer> : IAsyncLifetime
  where TContainer : ServerContainer, new()
{
  private static readonly TestClientInfo ClientInfo = new("test", "3.0.0");
  protected TContainer Container { get; } = new TContainer();

  public Task InitializeAsync() => Container.StartAsync();
  public async Task DisposeAsync() => await Container.DisposeAsync();

  [Fact]
  public async Task Initialize_WithScaffoldedSolution_ReturnsCapabilities()
  {
    using var solution = new TempContainerSolution();

    var response = await Container.Rpc.InvokeWithParameterObjectAsync<TestInitializeResponse>(
      "initialize",
      new List<TestInitializeRequest>
      {
      new(ClientInfo, new TestProjectInfo(
        Path.GetDirectoryName(solution.SolutionPath)!,
        solution.SolutionPath))
      });

    if (Container.SdkMajorVersion == 10)
    {
      Assert.True(response.Capabilities.SupportsSingleFileExecution);
    }
    else
    {
      Assert.False(response.Capabilities.SupportsSingleFileExecution);
    }

    Assert.NotNull(response);
    Assert.NotNull(response.ServerInfo);
    Assert.False(string.IsNullOrWhiteSpace(response.ServerInfo.Name));
    Assert.True(Version.TryParse(response.ServerInfo.Version, out _),
      $"ServerInfo.Version '{response.ServerInfo.Version}' is not a valid version string");
    Assert.NotEmpty(response.Capabilities.Routes);
    Assert.NotEmpty(response.Capabilities.ServerSentNotifications);
  }
}

public sealed class InitializeSdk8Linux : InitializeContainerTests<Sdk8LinuxContainer>;
public sealed class InitializeSdk9Linux : InitializeContainerTests<Sdk9LinuxContainer>;
public sealed class InitializeSdk10Linux : InitializeContainerTests<Sdk10LinuxContainer>;