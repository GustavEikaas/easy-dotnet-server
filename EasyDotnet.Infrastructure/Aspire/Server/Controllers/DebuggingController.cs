using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class DebuggingController
{
  private readonly TaskCompletionSource<bool> _debugSessionTcs = new();

  public Func<string, string?, bool, Task>? OnDebugSessionStarted;

  [JsonRpcMethod("startDebugSession")]
  public async Task StartDebugSession(string token, string workingDirectory, string? projectFile, bool debug)
  {
    Console.WriteLine($"[{token}] Start debug session {projectFile}");
    _ = OnDebugSessionStarted?.Invoke(workingDirectory, projectFile, debug);
    _ = await _debugSessionTcs.Task; // DO NOT return immediately!

    // async startDebugSession(workingDirectory: string, projectFile: string | null, debug: boolean): Promise<void> {
    //     this.clearProgressNotification();
    //
    //     const debugConfiguration: AspireExtendedDebugConfiguration = {
    //         type: 'aspire',
    //         name: `Aspire: ${getRelativePathToWorkspace(projectFile ?? workingDirectory)}`,
    //         request: 'launch',
    //         program: projectFile ?? workingDirectory,
    //         noDebug: !debug,
    //     };
    //
    //     const workspaceFolder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(workingDirectory));
    //     const didDebugStart = await vscode.debug.startDebugging(workspaceFolder, debugConfiguration);
    //     if (!didDebugStart) {
    //         throw new Error(failedToStartDebugSession);
    //     }
    // }
  }

  [JsonRpcMethod("stopDebugging")]
  public void StopDebugging(string token)
  {
    Console.WriteLine($"[{token}] Stop debugging");
  }

  [JsonRpcMethod("writeDebugSessionMessage")]
  public void WriteDebugSessionMessage(string token, string message, bool stdout)
  {
    Console.WriteLine($"[{token}] Debug message ({(stdout ? "stdout" : "stderr")}): {message}");
  }

  public void CompleteDebugSession()
  {
    _debugSessionTcs.TrySetResult(true);
  }
}