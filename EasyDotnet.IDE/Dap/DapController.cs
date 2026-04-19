using EasyDotnet.Controllers;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Dap;

public sealed record BreakpointCandidatesRequest(string FilePath, int Line);

public class DapController : BaseController
{
  [JsonRpcMethod("dap/breakpoint-candidates", UseSingleObjectParameterDeserialization = true)]
  public async Task<List<BreakpointCandidate>> GetBreakpointCandidatesAsync(
      BreakpointCandidatesRequest request,
      CancellationToken cancellationToken)
  {
    return await BreakpointResolverService.GetCandidatesAsync(
        request.FilePath,
        request.Line,
        cancellationToken);
  }
}