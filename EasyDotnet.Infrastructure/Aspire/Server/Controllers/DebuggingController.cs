using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class DebuggingController(
  INetcoreDbgService netcoreDbgService,
  IMsBuildService msBuildService,
  ILogger<DebuggingController> logger, DcpServer dcpServer)
{
  private readonly Dictionary<string, TaskCompletionSource<bool>> _debugSessions = [];
  private System.Diagnostics.Process? _appHostProcess;

  [JsonRpcMethod("startDebugSession")]
  public async Task StartDebugSession(string token, string workingDirectory,
                                    string? projectFile, bool debug)
  {
    logger.LogInformation($"[{token}] StartDebugSession for {projectFile ?? workingDirectory}");

    if (string.IsNullOrEmpty(projectFile))
    {
      logger.LogWarning("No project file specified");
      return;
    }

    // This should ONLY be called for child services, not the AppHost
    // The AppHost is started via launchAppHost

    var sessionId = projectFile;
    var tcs = new TaskCompletionSource<bool>();
    _debugSessions[sessionId] = tcs;

    try
    {
      var project = await msBuildService.GetOrSetProjectPropertiesAsync(
        projectFile, null, "Debug", CancellationToken.None);

      if (!debug)
      {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
          FileName = "dotnet",
          Arguments = $"run --project \"{projectFile}\" --no-build",
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          WorkingDirectory = Path.GetDirectoryName(projectFile)!
        };
        // DEBUG_SESSION_RUN_MODE
        psi.Environment["DEBUG_SESSION_PORT"] = $"localhost:{dcpServer.Port}";
        psi.Environment["DEBUG_SESSION_TOKEN"] = dcpServer.Token;
        psi.Environment["DEBUG_SESSION_CERTIFICATE"] = dcpServer.CertificateBase64;

        var runSessionInfo = new
        {
          supported_launch_configurations = new[] { "project" }
        };

        // env.ASPIRE_EXTENSION_DEBUG_SESSION_ID = debugSessionId;
        // env.DCP_INSTANCE_ID_PREFIX = debugSessionId + '-';
        // env.DEBUG_SESSION_RUN_MODE = noDebug === false ? "Debug" : "NoDebug";
        // env.ASPIRE_EXTENSION_DEBUG_RUN_MODE = noDebug === false ? "Debug" : "NoDebug";
        // env.DEBUG_SESSION_INFO = JSON.stringify(getRunSessionInfo());
        // env.ASPIRE_EXTENSION_CAPABILITIES = getSupportedCapabilities().join(',');
        psi.Environment["DEBUG_SESSION_INFO"] = JsonSerializer.Serialize(runSessionInfo);
        psi.Environment["DEBUG_SESSION_RUN_MODE"] = "Debug";

        _appHostProcess = System.Diagnostics.Process.Start(psi);
        if (_appHostProcess == null)
        {
          throw new Exception("Failed to start AppHost");
        }

        _appHostProcess.OutputDataReceived += (_, e) =>
        {
          if (!string.IsNullOrEmpty(e.Data))
            Console.WriteLine($"[AppHost] {e.Data}");
        };

        _appHostProcess.ErrorDataReceived += (_, e) =>
        {
          if (!string.IsNullOrEmpty(e.Data))
            Console.Error.WriteLine($"[AppHost] {e.Data}");
        };

        _appHostProcess.BeginOutputReadLine();
        _appHostProcess.BeginErrorReadLine();

        Console.WriteLine($"AppHost started with PID {_appHostProcess.Id}");
        logger.LogInformation("Debug=false, starting without debugger");
        var x = new TaskCompletionSource<object>();
        await x.Task;
      }

      var binaryPath = "netcoredbg"; // TODO: get from config
      logger.LogInformation($"Starting debugger for {projectFile}");

      var port = await netcoreDbgService.Start(
        binaryPath, project, projectFile, null, null);

      logger.LogInformation($"Debugger started on port {port} for {projectFile}");

      // Wait until this service's debugging completes
      await tcs.Task;

      logger.LogInformation($"Debug session completed for {projectFile}");
    }
    catch (Exception ex)
    {
      logger.LogError(ex, $"Failed to start debug session for {projectFile}");
      throw;
    }
    finally
    {
      _debugSessions.Remove(sessionId);
    }
  }

  [JsonRpcMethod("stopDebugging")]
  public void StopDebugging(string token)
  {
    logger.LogInformation($"[{token}] StopDebugging - completing all debug sessions");

    // Kill AppHost if running
    if (_appHostProcess != null && !_appHostProcess.HasExited)
    {
      logger.LogInformation("Killing AppHost process");
      _appHostProcess.Kill();
      _appHostProcess.Dispose();
      _appHostProcess = null;
    }

    // Complete all debug sessions
    foreach (var tcs in _debugSessions.Values)
    {
      tcs.TrySetResult(true);
    }

    _debugSessions.Clear();
  }

  [JsonRpcMethod("writeDebugSessionMessage")]
  public void WriteDebugSessionMessage(string token, string message, bool stdout)
  {
    logger.LogInformation($"[{token}] [{(stdout ? "OUT" : "ERR")}] {message}");
  }
}