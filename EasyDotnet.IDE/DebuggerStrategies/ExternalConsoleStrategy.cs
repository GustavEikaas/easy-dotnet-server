using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.IDE.Types;
using EasyDotnet.IDE.Utils;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class ExternalConsoleStrategy(ILogger<ExternalConsoleStrategy> logger) : IDebugSessionStrategy
{
  private DotnetProject? _project;
  private int _pid;
  private JsonRpc? _rpc;
  private NamedPipeServerStream? _pipeServer;

  public async Task PrepareAsync(DotnetProject project, CancellationToken ct) => _project = project;

  public async Task TransformRequestAsync(InterceptableAttachRequest request, IDebuggerProxy proxy)
  {
    var pipeName = PipeUtils.GeneratePipeName();
    _pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    var extWindowPath = DebuggerPayloadLocator.GetExternalConsolePath();
    var hookPath = DebuggerPayloadLocator.GetStartupHookPath();

    var terminalArgs = new[] { "dotnet", "exec", extWindowPath, "--pipe", pipeName, "--hook", hookPath };
    var runInTerminalReq = RunInTerminalRequest.Create(terminalArgs);
    runInTerminalReq.Arguments.Cwd = _project!.ProjectDir;

    logger.LogInformation("Sending runInTerminal request to Neovim");
    var termResponse = await proxy.RunClientRequestAsync(runInTerminalReq, CancellationToken.None);
    //TODO: deserialize termResponse and capture pid and termPid for dispose? (or is that entirely client responsibility?)

    if (!termResponse.Success)
    {
      throw new InvalidOperationException($"Neovim failed to launch terminal: {termResponse.Message}");
    }

    logger.LogInformation("runInTerminal response from Neovim");

    logger.LogInformation("Waiting for external console to connect on pipe {Pipe}", pipeName);
    await _pipeServer.WaitForConnectionAsync(CancellationToken.None);

    _rpc = new JsonRpc(_pipeServer);
#if DEBUG
    _rpc.TraceSource.Listeners.Add(new ConsoleTraceListener());
    _rpc.TraceSource.Switch.Level = SourceLevels.Verbose;
#endif
    _rpc.StartListening();

    logger.LogInformation("Sending initialize to external console");
    var res = await _rpc.InvokeWithParameterObjectAsync<InitializeResponse>(
        "initialize",
        //TODO: env vars and cwd?
        new { Program = "dotnet", Args = new List<string>() { _project.TargetPath! } },
        CancellationToken.None);

    _pid = res.Pid;
    logger.LogInformation("Received attach PID {Pid} from external console", _pid);

    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.ProcessId = _pid;
    //TODO: Does cwd do anything in attach?
    if (_project?.ProjectDir is not null)
    {
      request.Arguments.Cwd = _project.ProjectDir;
    }
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
  }

  public void OnDebugSessionReady(DebugSession debugSession, IDebuggerProxy proxy)
  {
    logger.LogInformation("ConfigurationDone received. Giving netcoredbg a moment to bind breakpoints...");
    _ = Task.Run(async () =>
    {
      await Task.Delay(500);

      logger.LogInformation("Resuming runtime via Startup Hook RPC.");

      if (_rpc is not null)
      {
        await _rpc.InvokeAsync("resume");
      }
      else
      {
        logger.LogWarning("RPC connection is null, cannot resume the target process.");
      }
    });
  }
}

public sealed record InitializeResponse(int Pid);

public static class DebuggerPayloadLocator
{
  public static string GetExternalConsolePath()
  {
    var path = "";
#if DEBUG
    path = Path.GetFullPath(Path.Join(GetAssemblyDir(), "../../../../EasyDotnet.ExternalConsole/bin/Debug/net8.0/EasyDotnet.ExternalConsole.dll"));
#else
    path = Path.Combine(GetBaseDir(), "ExternalConsole", "net8.0", "EasyDotnet.ExternalConsole.dll");
#endif
    if (!File.Exists(path))
    {
      throw new Exception("ExternalConsole dll not found");
    }
    return path;
  }

  public static string GetStartupHookPath()
  {
    var path = "";
#if DEBUG
    path = Path.GetFullPath(Path.Join(GetAssemblyDir(), "../../../../EasyDotnet.StartupHook/bin/Debug/net6.0/EasyDotnet.StartupHook.dll"));
#else
    path = Path.Combine(GetBaseDir(), "StartupHook", "net6.0", "EasyDotnet.StartupHook.dll");
#endif
    if (!File.Exists(path))
    {
      throw new Exception("StartupHook dll not found");
    }
    return path;
  }

  private static string GetAssemblyDir()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    return Path.GetDirectoryName(assemblyLocation)
        ?? throw new InvalidOperationException("Unable to determine assembly directory");
  }

  private static string GetBaseDir() => Path.Combine(GetAssemblyDir(), "DebuggerPayloads");
}