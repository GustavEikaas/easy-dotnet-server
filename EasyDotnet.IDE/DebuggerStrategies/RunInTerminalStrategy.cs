using System.Diagnostics;
using System.Net.Sockets;
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
  string? cliArgs,
  ILogger<RunInTerminalStrategy> logger,
  IStartupHookService startupHookService,
  IHttpClientFactory httpClientFactory,
  ILaunchProfileService launchProfileService) : IDebugSessionStrategy
{
  private DotnetProject? _project;
  private int _pid;
  private JsonRpc? _rpc;
  private LaunchProfile? _activeProfile;
  private StartupHookSession? _hookSession;
  private Process? _browserProcess;

  private int _configurationDoneFlag;
  private int _pidReceivedFlag;
  private int _processConnectRequestPreparedFlag;
  private int _resumeInvoked;

  private static void SetFlag(ref int flag) => Interlocked.Exchange(ref flag, 1);
  private static bool IsSet(ref int flag) => Interlocked.CompareExchange(ref flag, 0, 0) == 1;

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
    ApplyWebProjectContentRoot(profileEnv, _project);
    _hookSession = startupHookService.CreateSession(profileEnv);

    var extraArgs = BuildCommandLineArgs();
    var terminalArgs = new List<string>() { "dotnet", _project.TargetPath! };
    terminalArgs.AddRange(extraArgs);

    var terminalKind = ExtractTerminalKind(request.Arguments.Console);
    var runInTerminalReq = RunInTerminalRequest.Create(terminalKind, [.. terminalArgs]);

    var cwd = !string.IsNullOrWhiteSpace(_activeProfile?.WorkingDirectory)
        ? DebugStrategyUtils.NormalizePath(
            DebugStrategyUtils.InterpolateVariables(_activeProfile.WorkingDirectory, _project))
        : Path.GetDirectoryName(_project.TargetPath) ?? _project.ProjectDir;

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
    SetFlag(ref _pidReceivedFlag);
    logger.LogInformation("Received attach PID {Pid} from Startup Hook", _pid);

    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.ProcessId = _pid;
    if (_project?.ProjectDir is not null)
    {
      request.Arguments.Cwd = cwd;
    }

    SetFlag(ref _processConnectRequestPreparedFlag);

    logger.LogInformation(
        "Prepared debug process-connect request (Pid: {Pid}). configurationDoneSeen={ConfigurationDoneSeen}",
        _pid,
        IsSet(ref _configurationDoneFlag));

    TryResumeIfReady();
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
    SetFlag(ref _configurationDoneFlag);

    logger.LogInformation(
        "ConfigurationDone observed. pidReceived={PidReceived}, requestPrepared={RequestPrepared}",
        IsSet(ref _pidReceivedFlag),
        IsSet(ref _processConnectRequestPreparedFlag));

    TryResumeIfReady();
    _ = TryOpenBrowser();
  }

  private void TryResumeIfReady()
  {
    if (!IsSet(ref _configurationDoneFlag)
        || !IsSet(ref _pidReceivedFlag)
        || !IsSet(ref _processConnectRequestPreparedFlag)
        || _hookSession is null)
    {
      return;
    }

    if (Interlocked.Exchange(ref _resumeInvoked, 1) == 1)
    {
      return;
    }

    try
    {
      logger.LogInformation("Resuming runtime via Startup Hook (Pid: {Pid}).", _pid);
      _hookSession.Resume();
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to resume runtime via Startup Hook.");
    }
  }

  private async Task TryOpenBrowser()
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
      if (Uri.TryCreate(launchUrl, UriKind.Absolute, out var absoluteUri))
      {
        fullUrl = absoluteUri.ToString();
      }
      else
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
    }

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var client = httpClientFactory.CreateClient();

    while (!cts.Token.IsCancellationRequested)
    {
      try
      {
        await client.GetAsync(fullUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        logger.LogInformation("Server at {Url} is live. Opening browser.", fullUrl);
        break;
      }
      catch (OperationCanceledException)
      {
        logger.LogWarning("Server at {Url} did not respond within 5 seconds. Opening browser anyway.", fullUrl);
        break;
      }
      catch (HttpRequestException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
      {
        logger.LogDebug("Server at {Url} not yet available, retrying...", fullUrl);
        await Task.Delay(250, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Could not reach {Url} before opening browser.", fullUrl);
        break;
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

  private static void ApplyWebProjectContentRoot(Dictionary<string, string> environment, DotnetProject project)
  {
    var isWebProject = project.UsingMicrosoftNETSdkWeb || project.UsingMicrosoftNETSdkStaticWebAssets;
    var hasProjectDirectory = !string.IsNullOrWhiteSpace(project.ProjectDir);
    var hasExplicitContentRoot = environment.ContainsKey("ASPNETCORE_CONTENTROOT");

    if (!isWebProject || !hasProjectDirectory || hasExplicitContentRoot)
    {
      return;
    }

    environment["ASPNETCORE_CONTENTROOT"] = project.ProjectDir!;
  }

  private string[] BuildCommandLineArgs()
  {
    var args = new List<string>();

    if (_activeProfile?.CommandLineArgs is not null && _project is not null)
      args.AddRange(DebugStrategyUtils.SplitCommandLineArgs(
        DebugStrategyUtils.InterpolateVariables(_activeProfile.CommandLineArgs, _project)));

    if (cliArgs is not null && _project is not null)
      args.AddRange(DebugStrategyUtils.SplitCommandLineArgs(
        DebugStrategyUtils.InterpolateVariables(cliArgs, _project)));

    return [.. args];
  }
}