using System;
using System.Threading.Tasks;
using EasyDotnet.Controllers;
using EasyDotnet.Infrastructure.Editor;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Editor;

public sealed record ProcessExitedEvent(Guid JobId, int ProcessId, int ExitCode);

public class TerminalController(EditorProcessManagerService editorProcessManagerService) : BaseController
{
  [JsonRpcMethod("processExited")]
  public async Task ProcessExitedHandler(Guid jobId, int exitCode) => editorProcessManagerService.CompleteJob(jobId, exitCode);
}
