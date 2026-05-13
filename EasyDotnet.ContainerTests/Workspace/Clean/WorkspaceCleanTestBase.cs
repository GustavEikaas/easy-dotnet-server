using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Workspace.Build;

namespace EasyDotnet.ContainerTests.Workspace.Clean;

public abstract class WorkspaceCleanTestBase<TContainer> : WorkspaceBuildTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  protected Task BeginClean() =>
    BeginCall(Container.Rpc.WorkspaceCleanAsync());
}