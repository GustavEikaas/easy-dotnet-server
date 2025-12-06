using System.Diagnostics;
using System.Text.Json;
using EasyDotnet.Aspire.Server;
using EasyDotnet.Aspire.Session;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Aspire.Services;

public class AspireService : IAspireService
{
  private readonly IDcpServer _dcpServer;
  private readonly IAspireSessionManager _sessionManager;
  private readonly AspireCliProcessFactory _processFactory;
  private readonly ILogger<AspireService> _logger;

  public AspireService(
      IDcpServer dcpServer,
      IAspireSessionManager sessionManager,
      AspireCliProcessFactory processFactory,
      ILogger<AspireService> logger)
  {
    _dcpServer = dcpServer;
    _sessionManager = sessionManager;
    _processFactory = processFactory;
    _logger = logger;
  }

  public async Task<AspireSession> StartAsync(
      string projectPath,
      CancellationToken cancellationToken = default)
  {
    // Check if session already exists
    var existingSession = _sessionManager.GetSession(projectPath);
    if (existingSession != null)
    {
      _logger.LogWarning(
          "Aspire session already exists for {ProjectPath}",
          projectPath);
      return existingSession;
    }

    _logger.LogInformation("Starting Aspire session for {ProjectPath}", projectPath);

    // Ensure DCP server is running
    await _dcpServer.EnsureStartedAsync(cancellationToken);


    // Create and start the Aspire CLI process
    var cliProcess = _processFactory.CreateProcess(
        projectPath,
        _dcpServer.Port,
        _dcpServer.Token);

    // Create the session
    var session = new AspireSession
    {
      ProjectPath = projectPath,
      AspireCliProcess = cliProcess,
      Token = _dcpServer.Token,
      StartedAt = DateTime.UtcNow,
      SessionCts = new CancellationTokenSource()
    };

    // Register the session
    _sessionManager.AddSession(session);

    _logger.LogInformation(
        "Aspire session started: Project={ProjectPath},  Port={Port}",
        projectPath,
        _dcpServer.Port);

    return session;
  }

  public async Task StopAsync(
      string projectPath,
      CancellationToken cancellationToken = default)
  {
    _logger.LogInformation("Stopping Aspire session for {ProjectPath}", projectPath);

    await _sessionManager.TerminateSessionAsync(projectPath, cancellationToken);

    _logger.LogInformation("Aspire session stopped for {ProjectPath}", projectPath);
  }

  public AspireSessionStatus? GetSessionStatus(string projectPath)
  {
    var session = _sessionManager.GetSession(projectPath);
    if (session == null)
    {
      return null;
    }

    if (session.AspireCliProcess.HasExited)
    {
      return session.AspireCliProcess.ExitCode == 0
          ? AspireSessionStatus.Stopped
          : AspireSessionStatus.Failed;
    }

    return AspireSessionStatus.Running;
  }
}

// ============================================================================

public class AspireCliProcessFactory(ILogger<AspireCliProcessFactory> logger)
{
  public Process CreateProcess(
      string projectPath,
      int dcpPort,
      string sessionToken)
  {
    var workingDirectory = Path.GetDirectoryName(projectPath)
        ?? throw new ArgumentException("Invalid project path", nameof(projectPath));

    var psi = new ProcessStartInfo
    {
      FileName = "aspire",
      Arguments = "run --start-debug-session",
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = workingDirectory
    };

    // Set DCP environment variables with session-specific token
    psi.Environment["DEBUG_SESSION_PORT"] = $"localhost:{dcpPort}";
    psi.Environment["DEBUG_SESSION_TOKEN"] = sessionToken;
    psi.Environment["DEBUG_SESSION_RUN_MODE"] = "Debug";
    psi.Environment["ASPIRE_EXTENSION_DEBUG_RUN_MODE"] = "Debug";

    // Set capabilities
    var capabilities = new[]
    {
      "project",
      "prompting",
      "baseline.v1",
      "secret-prompts.v1",
      "ms-dotnettools.csharp",
      "devkit",
      "ms-dotnettools.csdevkit"
    };
    psi.Environment["ASPIRE_EXTENSION_CAPABILITIES"] = string.Join(", ", capabilities);

    // Set session info
    var runSessionInfo = new
    {
      ProtocolsSupported = new[] { "2024-03-03", "2024-04-23", "2025-10-01" },
      SupportedLaunchConfigurations = capabilities,
    };
    psi.Environment["DEBUG_SESSION_INFO"] = JsonSerializer.Serialize(runSessionInfo);

    logger.LogInformation(
        "Starting Aspire CLI process: Port={Port}, Token={Token}, WorkingDir={WorkingDir}",
        dcpPort,
        sessionToken,
        workingDirectory);

    var process = Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start Aspire CLI process");

    // Set up output handling
    SetupOutputHandling(process);

    return process;
  }

  private void SetupOutputHandling(Process process)
  {
    string? pendingUrl = null;

    process.OutputDataReceived += (_, e) =>
    {
      if (string.IsNullOrEmpty(e.Data))
        return;

      var line = e.Data;

      // Try to detect and open dashboard URLs
      if (pendingUrl == null)
      {
        var idx = line.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
          pendingUrl = line[idx..].Trim();
        }
      }
      else
      {
        pendingUrl += line.Trim();
      }

      if (pendingUrl != null &&
          Uri.TryCreate(pendingUrl, UriKind.Absolute, out var uri))
      {
        try
        {
          var psi = new ProcessStartInfo
          {
            FileName = uri.ToString(),
            UseShellExecute = true
          };
          Process.Start(psi);
          logger.LogInformation("Opened Aspire dashboard: {Url}", uri);
        }
        catch (Exception ex)
        {
          logger.LogWarning(ex, "Failed to open browser for {Url}", uri);
        }
        pendingUrl = null;
      }

      logger.LogDebug("[Aspire CLI] {Line}", line);
    };

    process.ErrorDataReceived += (_, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
      {
        logger.LogWarning("[Aspire CLI Error] {Line}", e.Data);
      }
    };

    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
  }
}