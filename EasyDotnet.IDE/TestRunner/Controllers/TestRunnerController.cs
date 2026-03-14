using EasyDotnet.Controllers;
using EasyDotnet.IDE.TestRunner.Service;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Controllers;

public class TestRunnerController(TestRunnerService service) : BaseController
{
  [JsonRpcMethod("testrunner/quickDiscover", UseSingleObjectParameterDeserialization = true)]
  public async Task QuickDiscoverAsync(InitializeRequest request, CancellationToken ct)
  {
    try { await service.QuickDiscoverAsync(request.SolutionPath, ct); }
    catch { }
  }

  [JsonRpcMethod("testrunner/initialize", UseSingleObjectParameterDeserialization = true)]
  public async Task<InitializeResult> InitializeAsync(InitializeRequest request, CancellationToken ct)
  {
    try { return await service.InitializeAsync(request.SolutionPath, ct); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress"))
    { throw new LocalRpcException(ex.Message) { ErrorCode = -32001 }; }
  }

  [JsonRpcMethod("testrunner/run", UseSingleObjectParameterDeserialization = true)]
  public async Task<OperationResult> RunAsync(NodeRequest request, CancellationToken ct)
  {
    try { return await service.RunAsync(request.Id, request.Source, ct); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress"))
    { throw new LocalRpcException(ex.Message) { ErrorCode = -32001 }; }
  }

  [JsonRpcMethod("testrunner/debug", UseSingleObjectParameterDeserialization = true)]
  public async Task<OperationResult> DebugAsync(NodeRequest request, CancellationToken ct)
  {
    try { return await service.DebugAsync(request.Id, request.Source, ct); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress"))
    { throw new LocalRpcException(ex.Message) { ErrorCode = -32001 }; }
  }

  [JsonRpcMethod("testrunner/invalidate", UseSingleObjectParameterDeserialization = true)]
  public async Task<OperationResult> InvalidateAsync(NodeRequest request, CancellationToken ct)
  {
    try { return await service.InvalidateAsync(request.Id, ct); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress"))
    { throw new LocalRpcException(ex.Message) { ErrorCode = -32001 }; }
  }

  // Read-only — no lock, returns immediately from DetailStore
  [JsonRpcMethod("testrunner/getResults", UseSingleObjectParameterDeserialization = true)]
  public GetResultsResult GetResults(NodeRequest request) =>
      service.GetResults(request.Id);

  [JsonRpcMethod("testrunner/syncFile", UseSingleObjectParameterDeserialization = true)]
  public SyncFileResult SyncFile(SyncFileRequest request) =>
          service.SyncFile(request);

  [JsonRpcMethod("testrunner/getBuildErrors", UseSingleObjectParameterDeserialization = true)]
  public Task GetBuildErrorsAsync(NodeRequest request) =>
       service.GetBuildErrorsAsync(request.Id);
}

public record InitializeRequest(string SolutionPath);
public record NodeRequest(string Id, string? Source = null);