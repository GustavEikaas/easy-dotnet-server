using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Nuget;

/// <summary>
/// Verifies <c>nuget/pack-and-push</c>:
///   1. Source picker is presented and only configured sources appear as choices.
///   2. End-to-end push to a local NuGet feed copies the .nupkg into the feed directory.
/// </summary>
public abstract class WorkspaceNugetPackAndPushTests<TContainer> : WorkspaceNugetTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task PackAndPush_DismissedSourcePicker_DoesNotPushAndReportsNoError()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("Lib.PushPick", p => p.AsPackable("Lib.PushPick", "1.2.3"))
      .WithLocalNugetFeed()
      .Build();
    await InitializeWorkspaceAsync(ws);

    var pushTask = BeginPackAndPush();
    var selection = await ReceiveSelectionAsync(_ => null);
    await pushTask;

    Assert.Equal("Pick NuGet source", selection.Prompt);
    Assert.Contains(selection.Choices, c => c.Id == "local");

    Assert.Empty(Directory.EnumerateFiles(ws.LocalNugetFeedDir!, "*.nupkg", SearchOption.AllDirectories));
  }

  [Fact]
  public async Task PackAndPush_ToLocalFeed_CopiesNupkgIntoFeed()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("Lib.PushOk", p => p.AsPackable("Lib.PushOk", "2.0.0"))
      .WithLocalNugetFeed()
      .Build();
    await InitializeWorkspaceAsync(ws);

    var pushTask = BeginPackAndPush();
    var packMessage = await ReceiveDisplayMessageAsync();
    await ReceiveSelectionAsync(req =>
      Array.Find(req.Choices, c => c.Id == "local")?.Id ?? req.Choices[0].Id);
    var pushMessage = await ReceiveDisplayMessageAsync();
    await pushTask;

    Assert.Equal("Pack succeeded.", packMessage);
    Assert.Contains("Pushed Lib.PushOk.2.0.0.nupkg", pushMessage);

    var pushed = Directory.EnumerateFiles(ws.LocalNugetFeedDir!, "Lib.PushOk*.nupkg", SearchOption.AllDirectories);
    Assert.NotEmpty(pushed);
  }
}

public sealed class WorkspaceNugetPackAndPushSdk10Linux : WorkspaceNugetPackAndPushTests<Sdk10LinuxContainer>;