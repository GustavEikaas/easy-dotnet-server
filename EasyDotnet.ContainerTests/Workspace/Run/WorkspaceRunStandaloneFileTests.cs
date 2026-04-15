using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Verifies that a standalone .cs file passed as filePath — when it lives outside any project
/// directory — appears in the workspace/run picker alongside the solution's runnable projects.
/// The picker should contain one entry per runnable project plus one for the standalone file.
/// </summary>
public abstract class WorkspaceRunStandaloneFileTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_WithStandaloneFileOutsideProject_IncludesFileOptionAlongsideRunnableProjects()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .SingleFileProject("Scripts/Hello.cs")
      .Build();
    await InitializeWorkspaceAsync(ws);

    // Return null — no choice made, we only want to inspect the picker contents.
    var runTask = BeginRun(filePath: ws.SingleFilePath);

    var selection = await ReceiveSelectionAsync(_ => null);
    await runTask;

    Assert.Contains(selection.Choices, c => c.Display.Contains("ProjectAlpha"));
    Assert.Contains(selection.Choices, c => c.Display.Contains("ProjectBeta"));

    if (Container.SdkMajorVersion >= 10)
    {
      Assert.Equal(3, selection.Choices.Length);
      Assert.Contains(selection.Choices, c => c.Id == "__singlefile__");
    }
    else
    {
      Assert.Equal(2, selection.Choices.Length);
      Assert.DoesNotContain(selection.Choices, c => c.Id == "__singlefile__");
    }
  }
}

public sealed class WorkspaceRunStandaloneFileSdk8Linux : WorkspaceRunStandaloneFileTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunStandaloneFileSdk9Linux : WorkspaceRunStandaloneFileTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunStandaloneFileSdk10Linux : WorkspaceRunStandaloneFileTests<Sdk10LinuxContainer>;
