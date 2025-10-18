using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public record EnvVar(string Name, string Value);

public class AppHostController(DcpServer dcpServer)
{
  private System.Diagnostics.Process? _appHostProcess;

  [JsonRpcMethod("launchAppHost")]
  public async Task LaunchAppHost(string token, string projectFile,
                                  List<string> arguments,
                                  List<EnvVar> environment,
                                  bool debug)
  {
    Console.WriteLine($"[{token}] LaunchAppHost: {projectFile}, Debug: {debug}");

    var psi = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = $"run --project \"{projectFile}\" --no-build",
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = Path.GetDirectoryName(projectFile)!
    };

    // CRITICAL: Tell AppHost where the DCP server is
    psi.Environment["DEBUG_SESSION_PORT"] = $"localhost:{dcpServer.Port}";
    psi.Environment["DEBUG_SESSION_TOKEN"] = dcpServer.Token;
    psi.Environment["DEBUG_SESSION_CERTIFICATE"] = dcpServer.CertificateBase64;

    // Add run session info
    var runSessionInfo = new
    {
      supported_launch_configurations = new[] { "project" }
    };
    psi.Environment["DEBUG_SESSION_INFO"] = JsonSerializer.Serialize(runSessionInfo);

    // Apply environment variables from Aspire CLI
    foreach (var envVar in environment)
    {
      Console.WriteLine($"  Setting env: {envVar.Name}={envVar.Value}");
      psi.Environment[envVar.Name] = envVar.Value;
    }

    // Add any additional arguments
    if (arguments.Count > 0)
    {
      psi.Arguments += " -- " + string.Join(" ", arguments);
    }

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
  }
}