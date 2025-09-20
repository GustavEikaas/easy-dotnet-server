using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using EasyDotnet.Extensions;


namespace EasyDotnet.MTP;

public static class ProcessFactory
{
  public static IProcessHandle Start(ProcessConfiguration config, bool cleanDefaultEnvironmentVariableIfCustomAreProvided = false)
  {
    var fullPath = config.FileName; // Path.GetFullPath(startInfo.FileName);
    var workingDirectory = config.WorkingDirectory
        .OrDefault(Path.GetDirectoryName(config.FileName).OrDefault(Directory.GetCurrentDirectory()));

    ProcessStartInfo processStartInfo = new()
    {
      FileName = fullPath,
      Arguments = config.Arguments,
      WorkingDirectory = workingDirectory,
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = true,
    };

    if (config.EnvironmentVariables is not null)
    {
      if (cleanDefaultEnvironmentVariableIfCustomAreProvided)
      {
        processStartInfo.Environment.Clear();
        processStartInfo.EnvironmentVariables.Clear();
      }

      foreach (var kvp in config.EnvironmentVariables)
      {
        if (kvp.Value is null)
        {
          continue;
        }

        processStartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
      }
    }

    Process process = new()
    {
      StartInfo = processStartInfo,
      EnableRaisingEvents = true,
    };

    // ToolName and Pid are not populated until we start the process,
    // and once we stop the process we cannot retrieve the info anymore
    // so we start the process, try to grab the needed info and set it.
    // And then we give the call reference to ProcessHandle, but not to ProcessHandleInfo
    // so they can easily get the info, but cannot change it.
    ProcessHandleInfo processHandleInfo = new();
    ProcessHandle processHandle = new(process, processHandleInfo);

    if (config.OnExit != null)
    {
      process.Exited += (_, _) => config.OnExit.Invoke(processHandle, process.ExitCode);
    }

    if (config.OnStandardOutput != null)
    {
      process.OutputDataReceived += (s, e) =>
      {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
          config.OnStandardOutput(processHandle, e.Data);
        }
      };
    }

    if (config.OnErrorOutput != null)
    {
      process.ErrorDataReceived += (s, e) =>
      {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
          config.OnErrorOutput(processHandle, e.Data);
        }
      };
    }

    if (!process.Start())
    {
      throw new InvalidOperationException("Process failed to start");
    }

    try
    {
      processHandleInfo.ProcessName = process.ProcessName;
    }
    catch (InvalidOperationException)
    {
      // The associated process has exited.
      // https://learn.microsoft.com/dotnet/api/system.diagnostics.process.processname?view=net-7.0
    }

    processHandleInfo.Id = process.Id;

    if (config.OnStandardOutput != null)
    {
      process.BeginOutputReadLine();
    }

    if (config.OnErrorOutput != null)
    {
      process.BeginErrorReadLine();
    }

    return processHandle;
  }
}

public sealed class ProcessConfiguration(string fileName)
{
  public string FileName { get; } = fileName;

  public required string Arguments { get; init; }

  public string? WorkingDirectory { get; init; }

  public IDictionary<string, string>? EnvironmentVariables { get; init; }

  public Action<IProcessHandle, string>? OnErrorOutput { get; init; }

  public Action<IProcessHandle, string>? OnStandardOutput { get; init; }

  public Action<IProcessHandle, int>? OnExit { get; init; }
}

public interface IProcessHandle
{
  int Id { get; }

  string ProcessName { get; }

  int ExitCode { get; }

  TextWriter StandardInput { get; }

  TextReader StandardOutput { get; }

  void Dispose();

  void Kill();

  Task<int> StopAsync();

  Task<int> WaitForExitAsync();

  void WaitForExit();

  Task WriteInputAsync(string input);
}

public sealed class ProcessHandleInfo
{
  public string ProcessName { get; internal set; }

  public int Id { get; internal set; }
}
public sealed class ProcessHandle : IProcessHandle, IDisposable
{
  private readonly ProcessHandleInfo _processHandleInfo;
  private readonly Process _process;
  private bool _disposed;
  private int _exitCode;

  internal ProcessHandle(Process process, ProcessHandleInfo processHandleInfo)
  {
    _processHandleInfo = processHandleInfo;
    _process = process;
  }

  public string ProcessName => _processHandleInfo.ProcessName ?? "<unknown>";

  public int Id => _processHandleInfo.Id;

  public TextWriter StandardInput => _process.StandardInput;

  public TextReader StandardOutput => _process.StandardOutput;

  public int ExitCode => _process.ExitCode;

  public async Task<int> WaitForExitAsync()
  {
    if (!_disposed)
    {
      await _process.WaitForExitAsync();
    }

    return _exitCode;
  }

  public void WaitForExit() => _process.WaitForExit();

  public async Task<int> StopAsync()
  {
    if (_disposed)
    {
      return _exitCode;
    }

    KillSafe(_process);
    return await WaitForExitAsync();
  }

  public void Kill()
  {
    if (_disposed)
    {
      return;
    }

    KillSafe(_process);
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    lock (_process)
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
    }

    KillSafe(_process);
    _process.WaitForExit();
    _exitCode = _process.ExitCode;
    _process.Dispose();
  }

  public async Task WriteInputAsync(string input)
  {
    await _process.StandardInput.WriteLineAsync(input);
    await _process.StandardInput.FlushAsync();
  }

  private static void KillSafe(Process process)
  {
    try
    {
      process.Kill(true);
    }
    catch (InvalidOperationException)
    {
    }
    catch (NotSupportedException)
    {
    }
  }
}