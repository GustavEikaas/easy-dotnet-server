using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
#if RELEASE
using System.Linq;
using EasyDotnet.Infrastructure.Services;
#endif
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE;

public sealed class RoslynProxy(string clientPipeName, ILogger logger) : IAsyncDisposable
{
  private NamedPipeServerStream? _clientPipe;
  private Process? _roslynProcess;
  private Task? _clientToRoslynTask;
  private Task? _roslynToClientTask;
  private readonly CancellationTokenSource _cts = new();
  private Task? _disposeTask;

  public async Task StartAsync()
  {
    if (_disposeTask != null)
    {
      logger.LogInformation("Waiting for previous Roslyn session to fully dispose...");
      try
      {
        await _disposeTask;
      }
      catch { }
    }

    logger.LogInformation("Waiting for EasyDotnet client...");
    _clientPipe = new NamedPipeServerStream(
        clientPipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    await _clientPipe.WaitForConnectionAsync(_cts.Token);
    logger.LogInformation("Client connected");

    var roslynLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyDotnet",
        "RoslynLogs");
    Directory.CreateDirectory(roslynLogDir);
    logger.LogInformation("Logging to {dir}", roslynLogDir);
    var (fileName, arguments) = GetRoslynProcessStartInfo(roslynLogDir);
    _roslynProcess = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = fileName,
        Arguments = arguments,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      },
      EnableRaisingEvents = true
    };

    _roslynProcess.ErrorDataReceived += (s, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
        logger.LogError("[Roslyn-STDERR] {Message}", e.Data);
    };

    _roslynProcess.Start();
    _roslynProcess.BeginErrorReadLine();

    _clientToRoslynTask = Task.Run(() => PumpAsync(_clientPipe, _roslynProcess.StandardInput.BaseStream, _cts.Token));
    _roslynToClientTask = Task.Run(() => PumpAsync(_roslynProcess.StandardOutput.BaseStream, _clientPipe, _cts.Token));

    logger.LogInformation("Roslyn proxy attached and forwarding messages");
  }


  private static (string FileName, string Arguments) GetRoslynProcessStartInfo(string roslynLogDir)
  {
#if DEBUG
    return (@"C:\Users\Gustav\AppData\Local\nvim-data\mason\bin\roslyn.cmd", $"--stdio --logLevel=Information --extensionLogDirectory=\"{roslynLogDir}\"");
#else
    var roslynDllPath = RoslynLocator.GetRoslynDllPath();
    var analyzerArgs = string.Join(" ", 
        RoslynLocator.GetRoslynatorAnalyzers()
                      .Select(dll => $"--extension \"{dll}\""));

    var args = $"\"{roslynDllPath}\" --stdio --logLevel=Information --extensionLogDirectory=\"{roslynLogDir}\" {analyzerArgs}";
    return ("dotnet", args);
#endif
  }

  private async Task PumpAsync(Stream input, Stream output, CancellationToken token)
  {
    try
    {
      await input.CopyToAsync(output, 81920, token);
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Stream pump failed between {input} and {output}", input, output);
    }
    finally
    {
      logger.LogInformation("Disposing Roslyn LSP");
      await DisposeAsync();
    }
  }

  private void SafeDisposeProcess(Process? process, string processName)
  {
    if (process == null) return;

    try
    {
      if (!process.HasExited)
      {
        process.Kill();
        logger.LogInformation("Killed {processName} process", processName);
      }
      else
      {
        logger.LogInformation("{processName} process already exited", processName);
      }
    }
    catch (InvalidOperationException)
    {
      logger.LogInformation("{processName} process already exited", processName);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to kill {processName} process", processName);
    }
    finally
    {
      try
      {
        process.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to dispose {processName} process", processName);
      }
    }
  }

  public async ValueTask DisposeAsync()
  {
    _disposeTask = Task.Run(async () =>
    {
      _cts.Cancel();

      if (_clientToRoslynTask != null)
        await Task.WhenAny(_clientToRoslynTask, Task.Delay(2000));

      if (_roslynToClientTask != null)
        await Task.WhenAny(_roslynToClientTask, Task.Delay(2000));

      SafeDisposeProcess(_roslynProcess, "roslyn");

      if (_clientPipe is not null)
      {
        await _clientPipe.DisposeAsync();
      }

      _cts.Dispose();
    });

    await _disposeTask;
  }
}