using System.Diagnostics;
using EasyDotnet.AppWrapper.Contracts;
using Spectre.Console;
using StreamJsonRpc;

namespace EasyDotnet.AppWrapper;

public class AppWrapperHandler
{
  private readonly JsonRpc _rpc;
  private volatile Process? _currentProcess;

  public AppWrapperHandler(JsonRpc rpc)
  {
    _rpc = rpc;
    Console.CancelKeyPress += (_, e) =>
    {
      if (_currentProcess is { HasExited: false })
      {
        e.Cancel = true; // child is alive and already received SIGINT; keep AppWrapper running
      }
      // else: no child running — allow AppWrapper to exit naturally
    };
  }

  [JsonRpcMethod("appWrapper/run", UseSingleObjectParameterDeserialization = true)]
  public async Task RunAsync(RunAppCommand command, CancellationToken ct)
  {
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

    var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    _currentProcess = process;

    process.Start();

    try
    {
      await process.WaitForExitAsync(ct);
    }
    catch (OperationCanceledException)
    {
      await process.WaitForExitAsync(CancellationToken.None);
    }

    var exitCode = process.ExitCode;
    _currentProcess = null;

    Console.WriteLine();
    var codeText = exitCode == 134 ? "" : $" (code {exitCode})";
    AnsiConsole.MarkupLine($"[dim]App has exited{codeText}. This window will be reused.[/]");
    Console.WriteLine();

    try
    {
      await _rpc.NotifyWithParameterObjectAsync("appWrapper/exited", new AppExitedNotification(command.JobId, exitCode));
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"[AppWrapper] Failed to notify IDE of exit: {ex.Message}");
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