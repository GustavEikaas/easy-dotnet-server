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