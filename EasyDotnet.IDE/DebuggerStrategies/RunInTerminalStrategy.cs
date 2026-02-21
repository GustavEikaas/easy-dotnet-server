using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.IDE.Types;
using EasyDotnet.IDE.Utils;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class RunInTerminalStrategy(
  string? launchProfileName,
  ILogger<RunInTerminalStrategy> logger,
  ILaunchProfileService launchProfileService) : IDebugSessionStrategy
{
  private DotnetProject? _project;
  private int _pid;
  private JsonRpc? _rpc;
  private NamedPipeServerStream? _pipeServer;
  private LaunchProfile? _activeProfile;

  public async Task PrepareAsync(DotnetProject project, CancellationToken ct)
  {
    if (!string.IsNullOrEmpty(launchProfileName) &&
        launchProfileService.GetLaunchProfiles(project.MSBuildProjectFullPath!) is { } profiles &&
        profiles.TryGetValue(launchProfileName, out var profile))
    {
      _activeProfile = profile;
    }
    _project = project;
  }

  public async Task TransformRequestAsync(InterceptableAttachRequest request, IDebuggerProxy proxy)
  {
    if (_project == null) throw new InvalidOperationException("Strategy has not been prepared.");

    var pipeName = PipeUtils.GeneratePipeName();
    _pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    var extWindowPath = DebuggerPayloadLocator.GetExternalConsolePath();
    var hookPath = DebuggerPayloadLocator.GetStartupHookPath();

    var terminalArgs = new[] { "dotnet", "exec", extWindowPath, "--pipe", pipeName, "--hook", hookPath };
    var terminalKind = ExtractTerminalKind(request.Arguments.Console);
    var runInTerminalReq = RunInTerminalRequest.Create(terminalKind, terminalArgs);

    var cwd = !string.IsNullOrWhiteSpace(_activeProfile?.WorkingDirectory)
        ? DebugStrategyUtils.NormalizePath(DebugStrategyUtils.InterpolateVariables(_activeProfile.WorkingDirectory, _project))
        : _project.ProjectDir;

    runInTerminalReq.Arguments.Cwd = cwd;

    var env = DebugStrategyUtils.GetEnvironmentVariables(_activeProfile);

    logger.LogInformation("Sending runInTerminal request to Neovim");
    var termResponse = await proxy.RunClientRequestAsync(runInTerminalReq, CancellationToken.None);

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
        new
        {
          Program = "dotnet",
          Args = new List<string>() { _project.TargetPath! },
          Cwd = cwd,
          Env = (request.Arguments.Env ?? []).Concat(env).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        },
        CancellationToken.None);

    _pid = res.Pid;
    logger.LogInformation("Received attach PID {Pid} from external console", _pid);

    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.ProcessId = _pid;
    if (_project?.ProjectDir is not null)
    {
      request.Arguments.Cwd = cwd;
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
    logger.LogInformation("Resuming runtime via Startup Hook RPC.");

    if (_rpc is not null)
    {
      _ = _rpc.InvokeAsync("resume");
    }
    else
    {
      logger.LogWarning("RPC connection is null, cannot resume the target process.");
    }
  }

  private static RunInTerminalKind ExtractTerminalKind(string? consoleConfig)
  {
    if (string.IsNullOrWhiteSpace(consoleConfig))
    {
      return RunInTerminalKind.Internal;
    }

    return consoleConfig.Trim().ToLowerInvariant() switch
    {
      "externalterminal" => RunInTerminalKind.External,
      "integratedterminal" => RunInTerminalKind.Internal,
      _ => RunInTerminalKind.Internal
    };
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