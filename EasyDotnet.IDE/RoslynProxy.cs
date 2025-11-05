using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Services;

#if RELEASE
using System.Linq;
using EasyDotnet.Infrastructure.Services;
#endif
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE;

public sealed record RoslynProxyOptions(bool UseRoslynator, string[] AnalyzerAssemblies);

public sealed class RoslynProxy(string clientPipeName, ILogger logger) : IAsyncDisposable
{
  private NamedPipeServerStream? _clientPipe;
  private Process? _roslynProcess;
  private Task? _clientToRoslynTask;
  private Task? _roslynToClientTask;
  private readonly CancellationTokenSource _cts = new();
  private Task? _disposeTask;

  public async Task StartAsync(RoslynProxyOptions options)
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

    var roslynLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyDotnet",
        "RoslynLogs");

    Directory.CreateDirectory(roslynLogDir);

    logger.LogInformation("Logging to {dir}", roslynLogDir);

    var startInfo = GetRoslynProcessStartInfo(roslynLogDir, options);

    _roslynProcess = new Process
    {
      StartInfo = startInfo,
      EnableRaisingEvents = true
    };

    _roslynProcess.Exited += (s, e) =>
    {
      if (_roslynProcess != null)
      {
        logger.LogError("Roslyn process exited unexpectedly with code {ExitCode}", _roslynProcess.ExitCode);
      }
    };

    _roslynProcess.ErrorDataReceived += (s, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
        logger.LogError("[Roslyn-STDERR] {Message}", e.Data);
    };

    _roslynProcess.Start();
    _roslynProcess.BeginErrorReadLine();

    _ = Task.Run(async () =>
       {
         await _clientPipe.WaitForConnectionAsync(_cts.Token);
         logger.LogInformation("Client connected");

         _clientToRoslynTask = PumpAsync(_clientPipe, _roslynProcess.StandardInput.BaseStream, _cts.Token, "client");
         _roslynToClientTask = PumpAsync(_roslynProcess.StandardOutput.BaseStream, _clientPipe, _cts.Token, "roslyn");

         logger.LogInformation("Roslyn proxy attached and forwarding messages");
       });
  }


  private ProcessStartInfo GetRoslynProcessStartInfo(string roslynLogDir, RoslynProxyOptions options)
  {
    // #if DEBUG
    //     var psi = new ProcessStartInfo(
    //         @"C:\Users\gustav.eikaas\AppData\Local\nvim-data\mason\bin\roslyn.cmd")
    //     {
    //       RedirectStandardInput = true,
    //       RedirectStandardOutput = true,
    //       RedirectStandardError = true,
    //       UseShellExecute = false,
    //       CreateNoWindow = true
    //     };
    //
    //     psi.ArgumentList.Add("--stdio");
    //     psi.ArgumentList.Add("--logLevel=Information");
    //     psi.ArgumentList.Add("--extensionLogDirectory");
    //     psi.ArgumentList.Add(roslynLogDir);
    //
    //     return psi;
    // #else
    var roslynDllPath = RoslynLocator.GetRoslynDllPath();
    var razorDllPath = RoslynLocator.GetRazorDllPath();
    var razorTargetsPath = RoslynLocator.GetRazorTargetsPath();
    var psi = new ProcessStartInfo(roslynDllPath)
    {
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      WorkingDirectory = "C:/Users/Gustav/repo/aspire/aspire.Web"

    };
    psi.ArgumentList.Add("--stdio");
    psi.ArgumentList.Add("--logLevel=Information");
    psi.ArgumentList.Add("--extensionLogDirectory");
    psi.ArgumentList.Add(roslynLogDir);
    psi.ArgumentList.Add($"--razorSourceGenerator");
    psi.ArgumentList.Add(razorDllPath);
    psi.ArgumentList.Add($"--razorDesignTimePath");
    psi.ArgumentList.Add(razorTargetsPath);

    foreach (var dll in GetAnalyzers(options))
    {
      logger.LogInformation("[Roslyn]: Adding extension {ext}", dll);
      psi.ArgumentList.Add("--extension");
      psi.ArgumentList.Add(dll);
    }

    var commandString = $"{psi.FileName} {string.Join(' ', psi.ArgumentList.Select(arg => $"\"{arg}\""))}";
    logger.LogInformation("[Roslyn] Executing command: {command}", commandString);

    return psi;
    // #endif
  }

  private static IEnumerable<string> GetAnalyzers(RoslynProxyOptions options)
  {
    var roslynatorAnalyzers = options.UseRoslynator
        ? RoslynLocator.GetRoslynatorAnalyzers()
        : Enumerable.Empty<string>();

    var additionalAnalyzers = options.AnalyzerAssemblies ?? [];

    return roslynatorAnalyzers.Concat(additionalAnalyzers).Concat([RoslynLocator.GetRazorExtensionDllPath()]);
  }

  private async Task PumpAsync(Stream input, Stream output, CancellationToken token, string pumpName)
  {
    try
    {
      await input.CopyToAsync(output, 81920, token);
      logger.LogInformation("{pumpName} finished naturally", pumpName);
    }
    catch (OperationCanceledException)
    {
      logger.LogInformation("{pumpName} canceled", pumpName);
    }
    catch (IOException ioEx)
    {
      logger.LogWarning(ioEx, "{pumpName} failed due to pipe closure", pumpName);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "{pumpName} failed unexpectedly", pumpName);
    }
    finally
    {
      logger.LogInformation("Disposing Roslyn LSP due to {pumpName} termination", pumpName);
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