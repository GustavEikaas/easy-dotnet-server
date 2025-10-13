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

         _clientToRoslynTask = PumpAsync(_clientPipe, _roslynProcess.StandardInput.BaseStream, _cts.Token);
         _roslynToClientTask = PumpAsync(_roslynProcess.StandardOutput.BaseStream, _clientPipe, _cts.Token);

         logger.LogInformation("Roslyn proxy attached and forwarding messages");
       });
  }


  private static ProcessStartInfo GetRoslynProcessStartInfo(string roslynLogDir, RoslynProxyOptions options)
  {
#if DEBUG
    var psi = new ProcessStartInfo(
        @"C:\Users\gustav.eikaas\AppData\Local\nvim-data\mason\bin\roslyn.cmd")
    {
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    psi.ArgumentList.Add("--stdio");
    psi.ArgumentList.Add("--logLevel=Information");
    psi.ArgumentList.Add("--extensionLogDirectory");
    psi.ArgumentList.Add(roslynLogDir);

    return psi;
#else
    var roslynDllPath = RoslynLocator.GetRoslynDllPath();
    var psi = new ProcessStartInfo("dotnet")
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    psi.ArgumentList.Add(roslynDllPath);
    psi.ArgumentList.Add("--stdio");
    psi.ArgumentList.Add("--logLevel=Information");
    psi.ArgumentList.Add("--extensionLogDirectory");
    psi.ArgumentList.Add(roslynLogDir);

    foreach (var dll in GetAnalyzers(options))
    {
        psi.ArgumentList.Add("--extension");
        psi.ArgumentList.Add(dll);
    }

    return psi;
#endif
  }

  private static IEnumerable<string> GetAnalyzers(RoslynProxyOptions options)
  {
    var roslynatorAnalyzers = options.UseRoslynator
        ? RoslynLocator.GetRoslynatorAnalyzers()
        : Enumerable.Empty<string>();

    var additionalAnalyzers = options.AnalyzerAssemblies ?? [];

    return roslynatorAnalyzers.Concat(additionalAnalyzers);
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