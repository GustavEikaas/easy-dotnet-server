using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Initialize;

public abstract class InitializeContainerTests<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Initialize_WithScaffoldedSolution_ReturnsCapabilities()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();

    var response = await InitializeWorkspaceAsync(ws);


    Assert.NotNull(response);
    Assert.NotNull(response.ServerInfo);
    Assert.False(string.IsNullOrWhiteSpace(response.ServerInfo.Name));
    Assert.True(Version.TryParse(response.ServerInfo.Version, out _),
      $"ServerInfo.Version '{response.ServerInfo.Version}' is not a valid version string");
    Assert.NotEmpty(response.Capabilities.Routes);
    Assert.NotEmpty(response.Capabilities.ServerSentNotifications);

    if (Container.SdkMajorVersion == 10)
    {
      Assert.True(response.Capabilities.SupportsSingleFileExecution);
    }
    else
    {
      Assert.False(response.Capabilities.SupportsSingleFileExecution);
    }
  }
}

public sealed class InitializeSdk8Linux : InitializeContainerTests<Sdk8LinuxContainer>;
public sealed class InitializeSdk9Linux : InitializeContainerTests<Sdk9LinuxContainer>;
public sealed class InitializeSdk10Linux : InitializeContainerTests<Sdk10LinuxContainer>;