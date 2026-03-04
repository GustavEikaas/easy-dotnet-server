using System.Diagnostics;
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
  IStartupHookService startupHookService,
  ILaunchProfileService launchProfileService) : IDebugSessionStrategy
{
  private DotnetProject? _project;
  private int _pid;
  private JsonRpc? _rpc;
  private LaunchProfile? _activeProfile;
  private StartupHookSession? _hookSession;
  private Process? _browserProcess;

  private readonly JsonSerializerOptions _jsonSerializerOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  public async Task PrepareAsync(DotnetProject project, CancellationToken ct)
  {
    _activeProfile = launchProfileService.GetLaunchProfile(project.MSBuildProjectFullPath!, launchProfileName);
    _project = project;
  }

  public async Task TransformRequestAsync(InterceptableAttachRequest request, IDebuggerProxy proxy)
  {
    if (_project == null) throw new InvalidOperationException("Strategy has not been prepared.");

    var profileEnv = DebugStrategyUtils.GetEnvironmentVariables(_activeProfile);
    _hookSession = startupHookService.CreateSession(profileEnv);

    var extraArgs = BuildCommandLineArgs();
    var terminalArgs = new List<string>() { "dotnet", _project.TargetPath! };
    terminalArgs.AddRange(extraArgs);

    var terminalKind = ExtractTerminalKind(request.Arguments.Console);
    var runInTerminalReq = RunInTerminalRequest.Create(terminalKind, [.. terminalArgs]);

    var cwd = !string.IsNullOrWhiteSpace(_activeProfile?.WorkingDirectory)
        ? DebugStrategyUtils.NormalizePath(DebugStrategyUtils.InterpolateVariables(_activeProfile.WorkingDirectory, _project))
        : _project.ProjectDir;

    runInTerminalReq.Arguments.Cwd = cwd;
    runInTerminalReq.Arguments.Env = _hookSession.EnvironmentVariables;


    logger.LogInformation("Sending runInTerminal request to Neovim: {payload}", JsonSerializer.Serialize(runInTerminalReq, _jsonSerializerOptions));
    var termResponse = await proxy.RunClientRequestAsync(runInTerminalReq, CancellationToken.None);

    if (!termResponse.Success)
    {
      throw new InvalidOperationException($"Neovim failed to launch terminal: {termResponse.Message}");
    }
    logger.LogInformation("runInTerminal response from Neovim");

    _pid = await _hookSession.WaitForPidAsync();
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

    if (_hookSession != null)
    {
      await _hookSession.DisposeAsync();
      _hookSession = null;
    }

    if (_browserProcess != null)
    {
      try
      {
        _browserProcess.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to dispose browser process handle.");
      }
      finally
      {
        _browserProcess = null;
      }
    }
  }

  public void OnDebugSessionReady(DebugSession debugSession, IDebuggerProxy proxy)
  {
    logger.LogInformation("Resuming runtime via Startup Hook.");
    _hookSession?.Resume();
    TryOpenBrowser();
  }

  private void TryOpenBrowser()
  {
    if (_activeProfile?.LaunchBrowser != true) return;

    var applicationUrls = _activeProfile.ApplicationUrl?.Split(';', StringSplitOptions.RemoveEmptyEntries);
    var baseUrl = applicationUrls?.FirstOrDefault();

    if (string.IsNullOrWhiteSpace(baseUrl))
    {
      logger.LogWarning("LaunchBrowser is true, but no ApplicationUrl was found in the active profile.");
      return;
    }

    var fullUrl = baseUrl;
    var launchUrl = _activeProfile.LaunchUrl;

    if (!string.IsNullOrWhiteSpace(launchUrl))
    {
      try
      {
        var uriBuilder = new UriBuilder(baseUrl);
        var existingPath = uriBuilder.Path.TrimEnd('/');
        var newPath = launchUrl.TrimStart('/');
        uriBuilder.Path = $"{existingPath}/{newPath}";
        fullUrl = uriBuilder.ToString();
      }
      catch (UriFormatException ex)
      {
        logger.LogWarning(ex, "Failed to parse the application URL: {Url}", baseUrl);
      }
    }

    try
    {
      _browserProcess = BrowserHelper.OpenBrowser(fullUrl);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "An error occurred while trying to open the browser.");
    }
  }

  private static RunInTerminalKind ExtractTerminalKind(string? consoleConfig) =>
         consoleConfig?.Trim().ToLowerInvariant() switch
         {
           "externalterminal" => RunInTerminalKind.External,
           "integratedterminal" => RunInTerminalKind.Internal,
           _ => RunInTerminalKind.Internal
         };

  private string[] BuildCommandLineArgs()
  {
    if (_activeProfile?.CommandLineArgs is not null && _project is not null)
    {
      var interpolatedArgs = DebugStrategyUtils.InterpolateVariables(_activeProfile.CommandLineArgs, _project);
      return DebugStrategyUtils.SplitCommandLineArgs(interpolatedArgs);
    }
    return [];
  }
}