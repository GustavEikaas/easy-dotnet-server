using System.IO.Pipes;
using System.Text.Json;
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
  private LaunchProfile? _activeProfile;
  private NamedPipeServerStream? _hookPipeServer;

  private readonly JsonSerializerOptions _jsonSerializerOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

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

    var hookPath = StartupHookLocator.GetStartupHookPath();
    var extraArgs = BuildCommandLineArgs();
    var terminalArgs = new List<string>() { "dotnet", _project.TargetPath! };
    terminalArgs.AddRange(extraArgs);
    var terminalKind = ExtractTerminalKind(request.Arguments.Console);
    var runInTerminalReq = RunInTerminalRequest.Create(terminalKind, [.. terminalArgs]);

    var cwd = !string.IsNullOrWhiteSpace(_activeProfile?.WorkingDirectory)
        ? DebugStrategyUtils.NormalizePath(DebugStrategyUtils.InterpolateVariables(_activeProfile.WorkingDirectory, _project))
        : _project.ProjectDir;

    runInTerminalReq.Arguments.Cwd = cwd;

    var hookPipeName = PipeUtils.GeneratePipeName();
    _hookPipeServer = new NamedPipeServerStream(hookPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    var env = DebugStrategyUtils.GetEnvironmentVariables(_activeProfile);

    runInTerminalReq.Arguments.Env = BuildEnvironmentVariables(
        request.Arguments.Env,
        env,
        hookPath,
        hookPipeName);

    logger.LogInformation("Sending runInTerminal request to Neovim: {payload}", JsonSerializer.Serialize(runInTerminalReq, _jsonSerializerOptions));
    var termResponse = await proxy.RunClientRequestAsync(runInTerminalReq, CancellationToken.None);

    if (!termResponse.Success)
    {
      throw new InvalidOperationException($"Neovim failed to launch terminal: {termResponse.Message}");
    }
    logger.LogInformation("runInTerminal response from Neovim");

    await _hookPipeServer.WaitForConnectionAsync(CancellationToken.None);

    var pidBuffer = new byte[4];
    await _hookPipeServer.ReadExactlyAsync(pidBuffer, 0, 4, CancellationToken.None);
    _pid = BitConverter.ToInt32(pidBuffer, 0);

    logger.LogInformation("Received attach PID {Pid} from Startup Hook", _pid);

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

    if (_hookPipeServer != null)
    {
      await _hookPipeServer.DisposeAsync();
      _hookPipeServer = null;
    }
  }

  public void OnDebugSessionReady(DebugSession debugSession, IDebuggerProxy proxy)
  {
    logger.LogInformation("Resuming runtime via Startup Hook RPC.");
    ResumeProgram();
  }

  private void ResumeProgram()
  {
    if (_hookPipeServer?.IsConnected == true)
    {
      _hookPipeServer.WriteByte(1);
      _hookPipeServer.Flush();
    }
    else
    {
      throw new Exception("StartupHook is not connected, unable to resume program");
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

  private string[] BuildCommandLineArgs()
  {
    if (_activeProfile?.CommandLineArgs is not null && _project is not null)
    {
      var interpolatedArgs = DebugStrategyUtils.InterpolateVariables(_activeProfile.CommandLineArgs, _project);
      return DebugStrategyUtils.SplitCommandLineArgs(interpolatedArgs);
    }
    return [];
  }

  private static Dictionary<string, string> BuildEnvironmentVariables(
        Dictionary<string, string>? requestEnv,
        Dictionary<string, string>? profileEnv,
        string hookPath,
        string hookPipeName)
  {
    var finalEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (profileEnv != null)
    {
      foreach (var kvp in profileEnv)
      {
        finalEnv[kvp.Key] = kvp.Value;
      }
    }

    if (requestEnv != null)
    {
      foreach (var kvp in requestEnv)
      {
        finalEnv[kvp.Key] = kvp.Value;
      }
    }

    if (finalEnv.TryGetValue("DOTNET_STARTUP_HOOKS", out var existingHooks) && !string.IsNullOrWhiteSpace(existingHooks))
    {
      finalEnv["DOTNET_STARTUP_HOOKS"] = $"{hookPath}{Path.PathSeparator}{existingHooks}";
    }
    else
    {
      finalEnv["DOTNET_STARTUP_HOOKS"] = hookPath;
    }

    finalEnv["EASY_DOTNET_HOOK_PIPE"] = hookPipeName;

    return finalEnv;
  }
}