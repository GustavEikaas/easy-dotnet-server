using System.Diagnostics;
using System.IO.Pipes;
using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.IDE.Types;
using EasyDotnet.IDE.Utils;
using EasyDotnet.MsBuild;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class ExternalConsoleStrategy(ILogger<ExternalConsoleStrategy> logger) : IDebugSessionStrategy
{
  private DotnetProject? _project;
  private int _pid;
  private JsonRpc? _rpc;
  private NamedPipeServerStream? _pipeServer;
  private Process? _externalConsoleProcess;

  public async Task PrepareAsync(DotnetProject project, CancellationToken ct)
  {
    _project = project;
    var pipeName = PipeUtils.GeneratePipeName();

    _pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    _externalConsoleProcess = Process.Start(new ProcessStartInfo
    {
      FileName = "cmd.exe",
      ArgumentList = { "/k", "dotnet", "C:/Users/gusta/repo/easy-dotnet-server/EasyDotnet.ExternalConsole/bin/Debug/net8.0/EasyDotnet.ExternalConsole.dll", "--pipe", pipeName },
      UseShellExecute = true
    }) ?? throw new InvalidOperationException("Failed to start external console process");

    logger.LogInformation("Waiting for external console to connect on pipe {Pipe}", pipeName);
    await _pipeServer.WaitForConnectionAsync(ct);

    var rpc = new JsonRpc(_pipeServer);
    _rpc = rpc;
    rpc.TraceSource.Listeners.Add(new ConsoleTraceListener());
    rpc.TraceSource.Switch.Level = SourceLevels.Verbose;
    rpc.StartListening();

    logger.LogInformation("Sending initialize to external console");
    var res = await rpc.InvokeWithParameterObjectAsync<InitializeResponse>(
        "initialize",
        new { Program = "dotnet", Args = new List<string>() { project.TargetPath! } },
        ct);

    _pid = res.Pid;
    logger.LogInformation("Received attachDebugger for pid {Pid}", _pid);
  }

  public Task TransformRequestAsync(InterceptableAttachRequest request)
  {
    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.StopAtEntry = true;
    request.Arguments.ProcessId = _pid;
    if (_project?.ProjectDir is not null)
    {
      request.Arguments.Cwd = _project.ProjectDir;
    }
    return Task.CompletedTask;
  }

  public Task<int>? GetProcessIdAsync() => Task.FromResult(_pid);

  public async ValueTask DisposeAsync()
  {
    _rpc?.Dispose();
    _rpc = null;
    if (_pipeServer is not null)
    {
      await _pipeServer.DisposeAsync();
      _pipeServer = null;
    }
    if (_externalConsoleProcess is null) return;
    try
    {
      _externalConsoleProcess.Kill(true);
      await _externalConsoleProcess.WaitForExitAsync();
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to kill external console process");
    }
    _externalConsoleProcess.Dispose();
  }

  public void OnDebugSessionReady(DebugSession debugSession)
  {
    logger.LogInformation("Resuming runtime via Startup Hook RPC");

    if (_rpc is not null)
    {
      // Fire and forget the resume command to unblock the Startup Hook.
      // If you can change IDebugSessionStrategy to make this method async, 
      // you should absolutely do 'await _rpc.InvokeAsync("resume");' instead.
      _ = _rpc.InvokeAsync("resume");
    }
    else
    {
      logger.LogWarning("RPC connection is null, cannot resume the target process.");
    }
  }
}

public sealed record InitializeResponse(int Pid);