using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.IDE.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Roslyn;

public class RoslynController(RoslynService roslynService, EfQuerySqlService efQuerySqlService) : BaseController
{
  [JsonRpcMethod("roslyn/ef-generated-sql")]
  public async Task<EfGeneratedSqlResponse> GetEfGeneratedSql(string sourceFilePath, int line, int character, CancellationToken cancellationToken)
  {
    var result = await efQuerySqlService.GetGeneratedSqlAsync(sourceFilePath, line, character, cancellationToken);
    return new EfGeneratedSqlResponse(result.Success, result.Sql, result.ErrorMessage, result.TargetProject, result.StartupProject, result.StartupProjectSource, result.Warnings);
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