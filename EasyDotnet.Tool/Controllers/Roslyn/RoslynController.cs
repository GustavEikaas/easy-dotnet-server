using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Roslyn;

public class RoslynController(RoslynService roslynService) : BaseController
{

  [JsonRpcMethod("roslyn/bootstrap-file")]
  public async Task<BootstrapFileResultResponse> BootstrapFile(string filePath, Kind kind, bool preferFileScopedNamespace)
  {
    var success = await roslynService.BootstrapFile(filePath, kind, preferFileScopedNamespace, new CancellationToken());
    return new(success);
  }

  [JsonRpcMethod("roslyn/scope-variables")]
  public async Task<IAsyncEnumerable<VariableResultResponse>> GetVariablesFromScopes(string sourceFilePath, int lineNumber)
  {
    var res = await roslynService.AnalyzeAsync(sourceFilePath, lineNumber);
    return res.Select(x => new VariableResultResponse(x.Identifier, x.LineStart, x.LineEnd, x.ColumnStart, x.ColumnEnd)).AsAsyncEnumerable();
  }

  [JsonRpcMethod("roslyn/get-workspace-diagnostics")]
  public IAsyncEnumerable<DiagnosticMessage> GetDiagnostics(string targetPath, bool includeWarnings = false) =>
    roslynService.GetWorkspaceDiagnosticsAsync(targetPath, includeWarnings);
}