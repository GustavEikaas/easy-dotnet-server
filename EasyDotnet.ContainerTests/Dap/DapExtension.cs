using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Dap;

public static class DapExtensions
{
  public static Task<List<TestBreakpointCandidate>> DapBreakpointCandidatesAsync(
      this JsonRpc rpc,
      string filePath,
      int line)
      => rpc.InvokeWithParameterObjectAsync<List<TestBreakpointCandidate>>(
          "dap/breakpoint-candidates",
          new { filePath, line });
}

