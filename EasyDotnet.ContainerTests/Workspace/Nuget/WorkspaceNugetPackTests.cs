using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Nuget;

/// <summary>
/// Verifies <c>nuget/pack</c>:
///   1. Single packable project => auto-selects, builds, packs, success notification, .nupkg on disk.
///   2. Multiple packable projects => promptSelection shown with packable-only choices.
///   3. No packable projects => displayError, no build/pack work performed.
/// </summary>
public abstract class WorkspaceNugetPackTests<TContainer> : WorkspaceNugetTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Pack_WithSinglePackableProject_AutoSelectsAndProducesNupkg()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("Lib.Pack", p => p.AsPackable("Lib.Pack", "1.0.0"))
      .Build();
    await InitializeWorkspaceAsync(ws);

    var packTask = BeginPack();
    var message = await ReceiveDisplayMessageAsync();
    await packTask;

    Assert.Equal(0, SelectionCallCount);
    Assert.Equal("Pack succeeded.", message);

    var nupkg = Path.Combine(ws.Project("Lib.Pack").Dir, "bin", "Release", "Lib.Pack.1.0.0.nupkg");
    Assert.True(File.Exists(nupkg), $"Expected nupkg at {nupkg}");
  }

  [Fact]
  public async Task Pack_WithMultiplePackableProjects_ShowsSelectionWithOnlyPackable()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("Lib.Alpha", p => p.AsPackable("Lib.Alpha"))
      .WithProject("Lib.Beta", p => p.AsPackable("Lib.Beta"))
      .WithProject("App.NotPackable", p => p.AsNotPackable())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var packTask = BeginPack();
    var selection = await ReceiveSelectionAsync(_ => null);
    await packTask;

    Assert.Equal("Pick project to pack", selection.Prompt);
    Assert.Equal(2, selection.Choices.Length);
    Assert.Contains(selection.Choices, c => c.Display.Contains("Lib.Alpha"));
    Assert.Contains(selection.Choices, c => c.Display.Contains("Lib.Beta"));
    Assert.DoesNotContain(selection.Choices, c => c.Display.Contains("App.NotPackable"));
  }

  [Fact]
  public async Task Pack_WithNoPackableProjects_DisplaysError()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("App.One", p => p.AsNotPackable())
      .WithProject("App.Two", p => p.AsNotPackable())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var packTask = BeginPack();
    var error = await ReceiveDisplayErrorAsync();
    await packTask;

    Assert.Equal("No packable projects found", error);
    Assert.Equal(0, SelectionCallCount);
  }
}

public sealed class WorkspaceNugetPackSdk10Linux : WorkspaceNugetPackTests<Sdk10LinuxContainer>;