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

public class BuildHostFactory(ILogger<BuildHostFactory> logger, IClientService clientService, LogLevelState logLevelState, GlobalJsonService globalJsonService, IEditorService editorService)
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
    Process process;
    try
    {
      process = SpawnProcess(runtime, pipeName);
    }
    catch (InvalidOperationException ex)
    {
      logger.LogError(ex, "Failed to spawn BuildServer process.");
      try { await editorService.DisplayError($"BuildServer failed to start.\n{ex.Message}"); }
      catch (Exception notifyEx) { logger.LogWarning(notifyEx, "Failed to surface BuildServer startup error to client."); }
      throw;
    }

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
      if (string.IsNullOrEmpty(versionStr) || !Version.TryParse(versionStr, out var requested))
        return "";

      var policy = ParseRollForwardPolicy(globalJson?.Sdk?.RollForward);

      MSBuildLocator.AllowQueryAllRuntimeVersions = true;
      var sdks = MSBuildLocator.QueryVisualStudioInstances()
        .Where(i => i.DiscoveryType == DiscoveryType.DotNetSdk)
        .ToList();

      var selected = SelectSdk(sdks, requested, policy)
        ?? throw new InvalidOperationException(BuildSdkNotFoundMessage(versionStr, policy, sdks));

      var dotnetRoot = DeriveDotnetRoot(selected.MSBuildPath);
      var runtimeDir = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
      var runtime = (Directory.Exists(runtimeDir)
        ? Directory.EnumerateDirectories(runtimeDir)
            .Select(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : null)
            .OfType<Version>()
            .Where(v => v.Major == selected.Version.Major)
            .MaxBy(v => v)
        : null)
        ?? throw new InvalidOperationException(
            $"Selected SDK {selected.Version} at '{selected.MSBuildPath}' has no matching " +
            $".NET {selected.Version.Major} runtime under '{runtimeDir}'. Install the matching runtime and try again.");

      logger.LogInformation(
        "global.json (version={Version}, rollForward={Policy}) → SDK {Sdk}; pinning BuildServer to --fx-version {Runtime}",
        versionStr, policy, selected.Version, runtime);
      return $"--fx-version {runtime} ";
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

  private enum RollForwardPolicy { Disable, Patch, Feature, Minor, Major, LatestPatch, LatestFeature, LatestMinor, LatestMajor }

  private static RollForwardPolicy ParseRollForwardPolicy(string? name) =>
    name?.Trim().ToLowerInvariant() switch
    {
      null or "" => RollForwardPolicy.Patch,
      "disable" => RollForwardPolicy.Disable,
      "patch" => RollForwardPolicy.Patch,
      "feature" => RollForwardPolicy.Feature,
      "minor" => RollForwardPolicy.Minor,
      "major" => RollForwardPolicy.Major,
      "latestpatch" => RollForwardPolicy.LatestPatch,
      "latestfeature" => RollForwardPolicy.LatestFeature,
      "latestminor" => RollForwardPolicy.LatestMinor,
      "latestmajor" => RollForwardPolicy.LatestMajor,
      _ => throw new InvalidOperationException($"Unsupported global.json rollForward policy '{name}'."),
    };

  private static int FeatureBand(Version v) => v.Build / 100;

  private static bool IsLatest(RollForwardPolicy p) =>
    p is RollForwardPolicy.LatestPatch or RollForwardPolicy.LatestFeature
         or RollForwardPolicy.LatestMinor or RollForwardPolicy.LatestMajor;

  private static bool MatchesPolicy(Version current, Version requested, RollForwardPolicy policy)
  {
    if (policy == RollForwardPolicy.Disable)
      return false;

    switch (policy)
    {
      case RollForwardPolicy.Patch:
      case RollForwardPolicy.LatestPatch:
        if (current.Major != requested.Major || current.Minor != requested.Minor || FeatureBand(current) != FeatureBand(requested))
          return false;
        break;
      case RollForwardPolicy.Feature:
      case RollForwardPolicy.LatestFeature:
        if (current.Major != requested.Major || current.Minor != requested.Minor)
          return false;
        break;
      case RollForwardPolicy.Minor:
      case RollForwardPolicy.LatestMinor:
        if (current.Major != requested.Major)
          return false;
        break;
    }

    return current >= requested;
  }

  private static bool IsBetterMatch(Version current, Version? previous, RollForwardPolicy policy)
  {
    if (previous is null)
      return true;

    if (IsLatest(policy) ||
        (current.Major == previous.Major &&
         current.Minor == previous.Minor &&
         FeatureBand(current) == FeatureBand(previous)))
    {
      return current > previous;
    }

    return current < previous;
  }

  private static VisualStudioInstance? SelectSdk(IReadOnlyList<VisualStudioInstance> sdks, Version requested, RollForwardPolicy policy)
  {
    if (policy is RollForwardPolicy.Disable or RollForwardPolicy.Patch)
    {
      var exact = sdks.FirstOrDefault(s => s.Version == requested);
      if (exact is not null)
        return exact;
      if (policy == RollForwardPolicy.Disable)
        return null;
    }

    VisualStudioInstance? best = null;
    foreach (var sdk in sdks)
    {
      if (!MatchesPolicy(sdk.Version, requested, policy))
        continue;
      if (IsBetterMatch(sdk.Version, best?.Version, policy))
        best = sdk;
    }
    return best;
  }

  private static string BuildSdkNotFoundMessage(string requested, RollForwardPolicy policy, IReadOnlyList<VisualStudioInstance> sdks)
  {
    var installed = string.Join(", ", sdks.Select(s => s.Version.ToString()).Distinct().OrderBy(s => s));
    return $"A compatible .NET SDK was not found.\n" +
           $"Requested SDK version: {requested}\n" +
           $"Roll-forward policy: {policy}\n" +
           $"Installed SDKs: {(string.IsNullOrEmpty(installed) ? "(none)" : installed)}\n" +
           "Install a matching SDK or update global.json.";
  }

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