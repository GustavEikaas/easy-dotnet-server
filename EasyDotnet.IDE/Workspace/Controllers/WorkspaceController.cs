using EasyDotnet.Controllers;
using EasyDotnet.IDE.Workspace.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Workspace.Controllers;

public class WorkspaceController(WorkspaceService service, WorkspaceBuildService buildService, WorkspaceRestoreService restoreService, WorkspaceTestService testService) : BaseController
{
  [JsonRpcMethod("workspace/run", UseSingleObjectParameterDeserialization = true)]
  public async Task RunAsync(WorkspaceRunRequest request, CancellationToken ct) =>
      await service.RunAsync(request, ct);

  [JsonRpcMethod("workspace/debug", UseSingleObjectParameterDeserialization = true)]
  public async Task DebugAsync(WorkspaceDebugRequest request, CancellationToken ct) =>
      await service.DebugAsync(request, ct);

  [JsonRpcMethod("workspace/build", UseSingleObjectParameterDeserialization = true)]
  public async Task BuildAsync(WorkspaceBuildRequest request, CancellationToken ct) =>
      await buildService.BuildProjectAsync(request, ct);

  [JsonRpcMethod("workspace/build-solution", UseSingleObjectParameterDeserialization = true)]
  public async Task BuildSolutionAsync(WorkspaceBuildRequest request, CancellationToken ct) =>
      await buildService.BuildSolutionAsync(request, ct);

  [JsonRpcMethod("workspace/restore", UseSingleObjectParameterDeserialization = true)]
  public async Task RestoreAsync(WorkspaceRestoreRequest request, CancellationToken ct) =>
      await restoreService.RestoreAsync(request, ct);

  [JsonRpcMethod("workspace/test", UseSingleObjectParameterDeserialization = true)]
  public async Task TestAsync(WorkspaceTestRequest request, CancellationToken ct) =>
      await testService.TestProjectAsync(request, ct);

  [JsonRpcMethod("workspace/test-solution", UseSingleObjectParameterDeserialization = true)]
  public async Task TestSolutionAsync(WorkspaceTestRequest request, CancellationToken ct) =>
      await testService.TestSolutionAsync(request, ct);
}