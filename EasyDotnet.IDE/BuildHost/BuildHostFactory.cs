using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Logging;
using EasyDotnet.IDE.Utils;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.IDE.BuildHost;

public enum BuildServerRuntime
{
  Net472,
  Net80
}

public class BuildHostFactory(ILogger<BuildHostFactory> logger, IClientService clientService, LogLevelState logLevelState, GlobalJsonService globalJsonService)
{
  public async Task<(Process, JsonRpc)> StartServerAsync()
  {
    clientService.ThrowIfNotInitialized();

    var runtime = BuildServerRuntime.Net80;

    if (clientService.UseVisualStudio && OperatingSystem.IsWindows())
    {
      runtime = BuildServerRuntime.Net472;
    }

    logger.LogInformation("Spawning BuildServer using runtime: {Runtime}", runtime == BuildServerRuntime.Net472 ? "Visual Studio" : "SDK");

    var pipeName = PipeUtils.GeneratePipeName();
    var process = SpawnProcess(runtime, pipeName);
    //pipeName = "debugPipe";

    try
    {
      var clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
      logger.LogInformation("Connecting to pipe: {PipeName}...", pipeName);

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      await ConnectWithRetryAsync(clientStream, cts.Token);

      logger.LogInformation("Connected to BuildServer.");

      var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, CreateJsonMessageFormatter()));
      rpc.StartListening();

      return (process, rpc);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to connect to BuildServer. Killing process.");
      try { process.Kill(); } catch { }
      throw;
    }
  }

  private static JsonMessageFormatter CreateJsonMessageFormatter() => new()
  {
    JsonSerializer = { ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }}
  };

  private Process SpawnProcess(BuildServerRuntime runtime, string pipeName)
  {
    var coreFolder = Path.GetDirectoryName(BuildHostLocator.GetBuildServerCore());
    var startInfo = new ProcessStartInfo
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardError = true,
      RedirectStandardOutput = false,
      WorkingDirectory = coreFolder
    };
    var logLevel = logLevelState.Current.ToString();

    if (runtime == BuildServerRuntime.Net472)
    {
      startInfo.FileName = BuildHostLocator.GetBuildServerFramework();
      startInfo.Arguments = $"--pipe \"{pipeName}\" --log-level={logLevel}";
    }
    else
    {
      startInfo.FileName = "dotnet";
      var fxVersionArg = ResolveFxVersionArg();
#if DEBUG
      startInfo.Arguments = $"exec {fxVersionArg}\"{BuildHostLocator.GetBuildServerCore()}\" --pipe \"{pipeName}\" --log-level=Verbose";
#else
      startInfo.Arguments = $"exec {fxVersionArg}\"{BuildHostLocator.GetBuildServerCore()}\" --pipe \"{pipeName}\" --log-level={logLevel}";
#endif
    }

    logger.LogInformation("Starting buildserver with command {command}", $"{startInfo.FileName} {startInfo.Arguments}");

    var process = new Process { StartInfo = startInfo };

    process.ErrorDataReceived += (s, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
      {
        logger.LogDebug("[BuildServer-STDERR] {Msg}", e.Data);
      }
    };

    process.Start();
    process.BeginErrorReadLine();

    return process;
  }

  private static async Task ConnectWithRetryAsync(NamedPipeClientStream stream, CancellationToken token)
  {
    var retryDelayMs = 50;
    while (!token.IsCancellationRequested)
    {
      try
      {
        await stream.ConnectAsync(500, token);
        return;
      }
      catch (TimeoutException) { }
      catch (IOException) { }

      await Task.Delay(retryDelayMs, token);
      retryDelayMs = Math.Min(retryDelayMs * 2, 500);
    }
    throw new TimeoutException("Timed out waiting for BuildServer pipe.");
  }

  private string ResolveFxVersionArg()
  {
    try
    {
      var slnDir = clientService.ProjectInfo?.SolutionFile != null ? Path.GetDirectoryName(clientService.ProjectInfo?.SolutionFile) : null;
      var globalJson = slnDir != null ? globalJsonService.GetGlobalJson(slnDir) : globalJsonService.GetGlobalJson();
      var versionStr = globalJson?.Sdk?.Version;
      if (string.IsNullOrEmpty(versionStr))
        return "";

      var dotIndex = versionStr.IndexOf('.');
      if (dotIndex < 0 || !int.TryParse(versionStr[..dotIndex], out var major) || major <= 0)
        return "";

      MSBuildLocator.AllowQueryAllRuntimeVersions = true;
      var sdkInstance = MSBuildLocator.QueryVisualStudioInstances()
          .Where(i => i.DiscoveryType == DiscoveryType.DotNetSdk && i.Version.Major == major)
          .OrderByDescending(i => i.Version)
          .FirstOrDefault()
          ?? throw new InvalidOperationException($"global.json requires .NET {major} SDK but no matching SDK installation was found. Install the .NET {major} SDK and try again.");

      var dotnetRoot = DeriveDotnetRoot(sdkInstance.MSBuildPath);
      var runtimeDir = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");

      var best = (Directory.Exists(runtimeDir)
          ? Directory.EnumerateDirectories(runtimeDir)
              .Select(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : null)
              .OfType<Version>()
              .Where(v => v.Major == major)
              .MaxBy(v => v)
          : null)
          ?? throw new InvalidOperationException($"global.json requires .NET {major} but no matching runtime was found under '{runtimeDir}'. Install the .NET {major} runtime and try again.");

      logger.LogInformation("global.json requires .NET {Major}; pinning BuildServer to --fx-version {Version}", major, best);
      return $"--fx-version {best} ";
    }
    catch (InvalidOperationException)
    {
      throw;
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to resolve --fx-version from global.json; falling back to default rollForward");
      return "";
    }
  }

  /// <summary>
  /// Derives the dotnet install root from an MSBuildPath such as
  /// /usr/share/dotnet/sdk/8.0.415/ → /usr/share/dotnet/
  /// </summary>
  private static string DeriveDotnetRoot(string msBuildPath)
  {
    var normalized = msBuildPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var sdkVersionDir = Path.GetDirectoryName(normalized) ?? normalized;
    return Path.GetDirectoryName(sdkVersionDir) ?? sdkVersionDir;
  }

  private static class BuildHostLocator
  {
    public static string GetBuildServerFramework()
    {
#if DEBUG
      return Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "../../../../EasyDotnet.BuildServer/bin/Debug/net472/EasyDotnet.BuildServer.exe");
#else
      var basedir = GetBaseDir();
      return Path.Combine(basedir, "net472", "EasyDotnet.BuildServer.exe");
#endif
    }

    public static string GetBuildServerCore()
    {
#if DEBUG
      return Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "../../../../EasyDotnet.BuildServer/bin/Debug/net8.0/EasyDotnet.BuildServer.dll");
#else
      var basedir = GetBaseDir();
      return Path.Combine(basedir, "net8.0", "EasyDotnet.BuildServer.dll");
#endif
    }

    private static string GetBaseDir()
    {
      var assemblyLocation = Assembly.GetExecutingAssembly().Location;
      var toolExeDir = Path.GetDirectoryName(assemblyLocation) ?? throw new InvalidOperationException("Unable to determine assembly directory");
      return Path.Combine(toolExeDir, "BuildServer");
    }
  }
}