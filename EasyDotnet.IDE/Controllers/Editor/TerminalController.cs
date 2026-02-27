using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Editor;

public class TerminalController(IEditorProcessManagerService editorProcessManagerService) : BaseController
{
  [JsonRpcMethod("processExited")]
  public async Task ProcessExitedHandler(Guid jobId, int exitCode) => editorProcessManagerService.CompleteJob(jobId, exitCode);
}