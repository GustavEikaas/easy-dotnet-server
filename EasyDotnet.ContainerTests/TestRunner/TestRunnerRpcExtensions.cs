using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// Typed wrappers around the <c>testrunner/*</c> RPC calls. Parallels
/// <see cref="EasyDotnet.ContainerTests.Workspace.Test.WorkspaceTestExtensions"/>.
/// </summary>
public static class TestRunnerRpcExtensions
{
  public static Task TestRunnerInitializeAsync(this JsonRpc rpc, string solutionPath) =>
    rpc.InvokeWithParameterObjectAsync("testrunner/initialize", new { solutionPath });

  public static Task TestRunnerRunAsync(this JsonRpc rpc, string id, string? source = null) =>
    rpc.InvokeWithParameterObjectAsync("testrunner/run", new { id, source });

  public static Task<OperationResultDto> TestRunnerRunWithResultAsync(this JsonRpc rpc, string id) =>
    rpc.InvokeWithParameterObjectAsync<OperationResultDto>("testrunner/run", new { id });

  public static Task<GetResultsDto> TestRunnerGetResultsAsync(this JsonRpc rpc, string id) =>
    rpc.InvokeWithParameterObjectAsync<GetResultsDto>("testrunner/getResults", new { id });

  public static Task<SyncFileResultDto> TestRunnerSyncFileAsync(this JsonRpc rpc, string path, string content, int version = 0) =>
    rpc.InvokeWithParameterObjectAsync<SyncFileResultDto>("testrunner/syncFile", new { path, content, version });
}

public sealed record SyncFileResultDto(LineNumberUpdateDto[] Updates, int Version);

public sealed record LineNumberUpdateDto(string Id, int SignatureLine, int BodyStartLine, int EndLine);

public sealed record OperationResultDto(bool Success);

public sealed record GetResultsDto(
  bool Found,
  string[]? ErrorMessage,
  string[]? Stdout,
  StackFrameDto[]? Frames,
  StackFrameDto? FailingFrame,
  string? DurationDisplay);

public sealed record StackFrameDto(string OriginalText, string? File, int? Line, bool IsUserCode, bool IsFromTest);