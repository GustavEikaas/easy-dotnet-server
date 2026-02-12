using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.IDE.BuildHost;

public enum BuildServerRuntime
{
  Net472,
  Net80
}

public class BuildHostFactory(ILogger<BuildHostFactory> logger, IClientService clientService, CurrentLogLevel currentLogLevel)
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
    var logDirectory = currentLogLevel.LogDir;
    var logLevel = currentLogLevel.Loglevel.ToString();

    if (runtime == BuildServerRuntime.Net472)
    {
      startInfo.FileName = BuildHostLocator.GetBuildServerFramework();
      startInfo.Arguments = $"--pipe \"{pipeName}\" --log-level={logLevel} --logDirectory \"{logDirectory}\"";
    }
    else
    {
      startInfo.FileName = "dotnet";
      startInfo.Arguments = $"exec \"{BuildHostLocator.GetBuildServerCore()}\" --pipe \"{pipeName}\" --log-level={logLevel} --logDirectory \"{logDirectory}\"";
    }

    logger.LogInformation("Starting buildserver with command {command}", $"{startInfo.FileName} {startInfo.Arguments}");

    var process = new Process { StartInfo = startInfo };

    process.ErrorDataReceived += (s, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
        logger.LogDebug("[BuildServer-STDERR] {Msg}", e.Data);
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

  private static class BuildHostLocator
  {
    public static string GetBuildServerFramework()
    {
      var basedir = GetBaseDir();
      return Path.Combine(basedir, "net472", "EasyDotnet.BuildServer.exe");
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