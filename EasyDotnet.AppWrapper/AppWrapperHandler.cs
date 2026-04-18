using System.Diagnostics;
using EasyDotnet.AppWrapper.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.AppWrapper;

public class AppWrapperHandler(JsonRpc rpc)
{
  private Process? _currentProcess;

  [JsonRpcMethod("appWrapper/run", UseSingleObjectParameterDeserialization = true)]
  public async Task RunAsync(RunAppCommand command, CancellationToken ct)
  {
    var exitCode = -1;

    var startInfo = new ProcessStartInfo
    {
      FileName = command.Executable,
      UseShellExecute = false,
      WorkingDirectory = command.WorkingDirectory,
    };

    foreach (var arg in command.Arguments)
    {
      startInfo.ArgumentList.Add(arg);
    }

    foreach (var kvp in command.EnvironmentVariables)
    {
      startInfo.Environment[kvp.Key] = kvp.Value;
    }

    try
    {
      var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
      _currentProcess = process;

      process.Start();
      Console.Error.WriteLine($"[AppWrapper] Child process started (PID {process.Id}).");

      try
      {
        await process.WaitForExitAsync(ct);
      }
      catch (OperationCanceledException)
      {
        await process.WaitForExitAsync(CancellationToken.None);
      }

      exitCode = process.ExitCode;
      Console.WriteLine($"\n[easy-dotnet] App has exited (code {exitCode}). This window will be reused.\n");
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"[AppWrapper] Failed to run child process: {ex.Message}");
    }
    finally
    {
      _currentProcess = null;
      try
      {
        await rpc.NotifyWithParameterObjectAsync("appWrapper/exited", new AppExitedNotification(command.JobId, exitCode));
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine($"[AppWrapper] Failed to notify IDE of exit: {ex.Message}");
      }
    }
  }

  public void KillCurrentProcess()
  {
    try
    {
      _currentProcess?.Kill(entireProcessTree: true);
    }
    catch { }
  }

  [JsonRpcMethod("appWrapper/terminate")]
  public Task TerminateAsync()
  {
    Console.Error.WriteLine("[AppWrapper] Terminate requested by IDE.");
    KillCurrentProcess();
    return Task.CompletedTask;
  }
}