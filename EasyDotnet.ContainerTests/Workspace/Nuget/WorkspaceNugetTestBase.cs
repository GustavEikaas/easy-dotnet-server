using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Workspace.Build;

namespace EasyDotnet.ContainerTests.Workspace.Nuget;

/// <summary>
/// Base for nuget/pack + nuget/pack-and-push container tests.
/// Inherits the reverse-request handlers (promptSelection, displayError, displayMessage,
/// quickfix) from <see cref="WorkspaceBuildTestBase{TContainer}"/> since the new endpoints
/// drive the same client surface. Adds BeginPack/BeginPackAndPush wrappers that allow a
/// longer scope timeout because real <c>dotnet pack</c> runs on the server.
/// </summary>
public abstract class WorkspaceNugetTestBase<TContainer> : WorkspaceBuildTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static readonly TimeSpan PackScopeTimeout = TimeSpan.FromMinutes(5);

  protected Task BeginPack(string? filePath = null)
    => BeginCall(Container.Rpc.WorkspacePackAsync(filePath), PackScopeTimeout);

  protected Task BeginPackAndPush(string? filePath = null)
    => BeginCall(Container.Rpc.WorkspacePackAndPushAsync(filePath), PackScopeTimeout);
}