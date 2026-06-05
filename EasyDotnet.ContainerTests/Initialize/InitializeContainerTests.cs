using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Initialize;

public abstract class InitializeContainerTests<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private readonly Channel<TestSolutionProjectsLoadedNotification> _projectsLoaded =
    Channel.CreateUnbounded<TestSolutionProjectsLoadedNotification>();

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
    Assert.Contains("solution/projects-loaded", response.Capabilities.ServerSentNotifications);

    if (Container.SdkMajorVersion == 10)
    {
      Assert.True(response.Capabilities.SupportsSingleFileExecution);
    }
    else
    {
      Assert.False(response.Capabilities.SupportsSingleFileExecution);
    }
  }

  [Fact]
  public async Task Initialize_WithScaffoldedSolution_NotifiesWhenProjectsLoaded()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();

    await InitializeWorkspaceAsync(ws);

    await _projectsLoaded.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromMinutes(3));
  }

  [Fact]
  public async Task Initialize_WithoutSolution_DoesNotNotifyProjectsLoaded()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("ProjectAlpha")
      .Build();

    await InitializeWorkspaceAsync(ws);

    var readTask = _projectsLoaded.Reader.ReadAsync().AsTask();
    var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(3)));

    Assert.NotSame(readTask, completed);
  }

  public override void SolutionProjectsLoaded(TestSolutionProjectsLoadedNotification notification)
  {
    _projectsLoaded.Writer.TryWrite(notification);
  }
}

[Collection(ContainerCollections.Sdk8Linux)]
public sealed class InitializeSdk8Linux : InitializeContainerTests<Sdk8LinuxContainer>;
[Collection(ContainerCollections.Sdk9Linux)]
public sealed class InitializeSdk9Linux : InitializeContainerTests<Sdk9LinuxContainer>;
[Collection(ContainerCollections.Sdk10Linux)]
public sealed class InitializeSdk10Linux : InitializeContainerTests<Sdk10LinuxContainer>;