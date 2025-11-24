using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Infrastructure.Dap;
using EasyDotnet.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server;

public class AspireServerContext
{
  public required JsonRpc RpcServer { get; init; }
  public required DcpServer DcpServer { get; init; }
  public required System.Diagnostics.Process AspireCliProcess { get; init; }
  public required X509Certificate2 Certificate { get; init; }
  public required string Token { get; init; }
}

public static class AspireServer
{
  public static async Task CreateAndStartAsync(
      string projectPath,
      INetcoreDbgService netcoreDbgService,
      IClientService clientService,
      IMsBuildService msBuildService,
      ILogger<DcpServer> dcpLogger,
      ILogger<DebuggerProxy> debuggerProxyLogger,
      ILogger<NetcoreDbgService> logger2,
      CancellationToken cancellationToken = default)
  {
    var dcpServer = await DcpServer.CreateAsync(dcpLogger, netcoreDbgService, msBuildService, clientService, debuggerProxyLogger, logger2, cancellationToken);
    Console.WriteLine($"DCP server listening on port {dcpServer.Port}");

    StartAspireCliProcess(projectPath, dcpServer);

  }

  private static System.Diagnostics.Process StartAspireCliProcess(string projectPath, DcpServer dcpServer)
  {
    var psi = new ProcessStartInfo
    {
      FileName = "aspire",
      Arguments = "run --start-debug-session",
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = Path.GetDirectoryName(projectPath)
    };

    psi.Environment["DEBUG_SESSION_PORT"] = $"localhost:{dcpServer.Port}";
    psi.Environment["DEBUG_SESSION_TOKEN"] = dcpServer.Token;
    // psi.Environment["DEBUG_SESSION_CERTIFICATE"] = dcpServer.CertificateBase64;

    // env.DCP_INSTANCE_ID_PREFIX = debugSessionId + '-';
    psi.Environment["DEBUG_SESSION_RUN_MODE"] = "Debug";
    psi.Environment["ASPIRE_EXTENSION_DEBUG_RUN_MODE"] = "Debug";
    var cap = new[] { "project", "prompting", "baseline.v1", "secret-prompts.v1", "ms-dotnettools.csharp", "devkit", "ms-dotnettools.csdevkit" };
    psi.Environment["ASPIRE_EXTENSION_CAPABILITIES"] = string.Join(", ", cap);

    var runSessionInfo = new
    {
      ProtocolsSupported = new[] { "2024-03-03", "2024-04-23", "2025-10-01" },
      SupportedLaunchConfigurations = cap,
    };
    psi.Environment["DEBUG_SESSION_INFO"] = JsonSerializer.Serialize(runSessionInfo);

    Console.WriteLine($"Starting Aspire CLI with DCP server at localhost:{dcpServer.Port}");

    var cliProcess = System.Diagnostics.Process.Start(psi) ?? throw new Exception("Failed to start Aspire CLI");

    cliProcess.OutputDataReceived += (_, e) =>
      {
        if (!string.IsNullOrEmpty(e.Data))
          Console.WriteLine("[Aspire CLI] " + e.Data);
      };

    cliProcess.ErrorDataReceived += (_, e) =>
        {
          if (!string.IsNullOrEmpty(e.Data))
            Console.Error.WriteLine("[Aspire CLI] " + e.Data);
        };

    cliProcess.BeginOutputReadLine();
    cliProcess.BeginErrorReadLine();

    return cliProcess;
  }
}