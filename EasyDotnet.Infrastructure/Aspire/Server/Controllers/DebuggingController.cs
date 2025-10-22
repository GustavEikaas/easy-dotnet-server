using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class DebuggingController(
  INetcoreDbgService netcoreDbgService,
  IClientService clientService,
  IMsBuildService msBuildService,
  ILogger<DebuggingController> logger, DcpServer dcpServer)
{
  private readonly Dictionary<string, TaskCompletionSource<bool>> _debugSessions = [];
  private System.Diagnostics.Process? _appHostProcess;

  [JsonRpcMethod("startDebugSession")]
  public async Task StartDebugSession(string token, string workingDirectory, string? projectFile, bool debug)
  {
    logger.LogInformation("[{Token}] StartDebugSession: {ProjectFile}, debug={Debug}", token, projectFile, debug);

    if (string.IsNullOrEmpty(projectFile))
    {
      logger.LogWarning("No project file specified");
      return;
    }

    // Check if this is the AppHost
    var project = await msBuildService.GetOrSetProjectPropertiesAsync(
      projectFile, null, "Debug", CancellationToken.None);

    if (project.IsAspireHost)
    {
      // This is the AppHost - start it with the DCP server configured
      logger.LogInformation("Starting AppHost: {ProjectFile}", projectFile);

      var psi = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "dotnet",
        Arguments = $"run --project \"{projectFile}\" --no-build",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = workingDirectory
      };

      // CRITICAL: Set DCP server info so the AppHost uses IDE execution mode
      psi.Environment["DEBUG_SESSION_PORT"] = $"localhost:{dcpServer.Port}";
      psi.Environment["DEBUG_SESSION_TOKEN"] = dcpServer.Token;
      psi.Environment["DEBUG_SESSION_CERTIFICATE"] = dcpServer.CertificateBase64;

      var runSessionInfo = new
      {
        supported_launch_configurations = new[] { "project" }
      };
      psi.Environment["DEBUG_SESSION_INFO"] = JsonSerializer.Serialize(runSessionInfo);

      logger.LogInformation("Starting AppHost with DEBUG_SESSION_PORT=localhost:{Port}", dcpServer.Port);

      _appHostProcess = System.Diagnostics.Process.Start(psi);
      if (_appHostProcess == null)
      {
        throw new Exception("Failed to start AppHost");
      }

      _appHostProcess.OutputDataReceived += (_, e) =>
      {
        if (!string.IsNullOrEmpty(e.Data))
          logger.LogInformation("[AppHost] {Output}", e.Data);
      };

      _appHostProcess.ErrorDataReceived += (_, e) =>
      {
        if (!string.IsNullOrEmpty(e.Data))
          logger.LogError("[AppHost] {Error}", e.Data);
      };

      _appHostProcess.BeginOutputReadLine();
      _appHostProcess.BeginErrorReadLine();

      logger.LogInformation("AppHost started with PID {Pid}", _appHostProcess.Id);

      // Wait for the AppHost to exit
      await _appHostProcess.WaitForExitAsync();
      logger.LogInformation("AppHost exited with code {ExitCode}", _appHostProcess.ExitCode);
    }
    else
    {
      // This is a child service - this should NOT be called because DCP should use the HTTP server
      logger.LogWarning("StartDebugSession called for non-AppHost project: {ProjectFile}", projectFile);
      logger.LogWarning("This suggests DCP is not using the IDE execution endpoint properly");
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