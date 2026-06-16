using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using EasyDotnet.IDE.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Aspire;

/// <summary>
/// Spawns the isolated <c>EasyDotnet.Aspire</c> host process and connects to it over
/// a named pipe (mirrors <see cref="BuildHost.BuildHostFactory"/>). Registers the
/// editor callback target so the Aspire host can drive managed runs.
/// </summary>
public class AspireHostFactory(ILogger<AspireHostFactory> logger, AspireRunService runService)
{
  public async Task<(Process, JsonRpc)> StartServerAsync()
  {
    var pipeName = PipeUtils.GeneratePipeName();
    var process = SpawnProcess(pipeName);

    try
    {
      var clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
      logger.LogInformation("Connecting to Aspire host pipe: {PipeName}...", pipeName);

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      await ConnectWithRetryAsync(clientStream, cts.Token);

      var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, CreateJsonMessageFormatter()));
      rpc.AddLocalRpcTarget(new AspireEditorCallbackTarget(runService, rpc));
      rpc.StartListening();

      logger.LogInformation("Connected to Aspire host.");
      return (process, rpc);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to connect to Aspire host. Killing process.");
      try { process.Kill(); } catch { }
      throw;
    }
  }

  private static JsonMessageFormatter CreateJsonMessageFormatter() => new()
  {
    JsonSerializer = { ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } }
  };

  private Process SpawnProcess(string pipeName)
  {
    var dllPath = AspireHostLocator.GetAspireHost();
    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = $"exec \"{dllPath}\" --pipe \"{pipeName}\"",
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardError = true,
      RedirectStandardOutput = false,
      WorkingDirectory = Path.GetDirectoryName(dllPath)
    };

    logger.LogInformation("Starting Aspire host with command {Command}", $"{startInfo.FileName} {startInfo.Arguments}");

    var process = new Process { StartInfo = startInfo };
    process.ErrorDataReceived += (_, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
      {
        logger.LogDebug("[Aspire-STDERR] {Msg}", e.Data);
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
    throw new TimeoutException("Timed out waiting for Aspire host pipe.");
  }

  private static class AspireHostLocator
  {
    // Mirrors AppWrapperLocator: PublishAspire emits Tools/Aspire/net8.0 which the IDE
    // csproj's Tools/** None glob packs to tools/Aspire/net8.0 in the tool package.
    public static string GetAspireHost()
    {
#if DEBUG
      var path = Path.GetFullPath(Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "../../../../EasyDotnet.Aspire/bin/Debug/net8.0/EasyDotnet.Aspire.dll"));
#else
      var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException("Unable to determine assembly directory");
      var path = Path.Combine(assemblyDir, "..", "..", "..", "tools", "Aspire", "net8.0", "EasyDotnet.Aspire.dll");
#endif
      if (!File.Exists(path))
      {
        throw new FileNotFoundException("Aspire host dll not found.", path);
      }
      return path;
    }
  }
}