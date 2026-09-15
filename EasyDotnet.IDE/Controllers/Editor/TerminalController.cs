using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Editor;

public class TerminalController(
  IEditorProcessManagerService editorProcessManagerService,
  IPtyTerminalService ptyTerminalService) : BaseController
{
  [JsonRpcMethod("processExited")]
  public async Task ProcessExitedHandler(Guid jobId, int exitCode) => editorProcessManagerService.CompleteJob(jobId, exitCode);

  [JsonRpcMethod("terminal/input")]
  public async Task TerminalInput(Guid jobId, byte[] data) => ptyTerminalService.Write(jobId, data);

  [JsonRpcMethod("terminal/resize")]
  public async Task TerminalResize(Guid jobId, int cols, int rows) => ptyTerminalService.Resize(jobId, cols, rows);

  [JsonRpcMethod("terminal/kill")]
  public async Task TerminalKill(Guid jobId) => ptyTerminalService.Kill(jobId);
}